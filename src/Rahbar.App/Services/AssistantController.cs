using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Rahbar.App.Audio;
using Rahbar.App.Capture;
using Rahbar.App.Native;
using Rahbar.App.Overlay;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Rahbar.Core.Pipeline;
using Rahbar.Core.Prompts;
using Rahbar.Core.Settings;
using Rahbar.Providers;
using Serilog;

namespace Rahbar.App.Services;

public enum AssistantState { Idle, Listening, Thinking, Speaking }

/// <summary>
/// Runs one question end to end:
/// hotkey down → screenshot + mic on → hotkey up → transcribe → ask brain → speak + point.
/// Esc at any moment stops speech and clears the screen.
/// </summary>
internal sealed class AssistantController : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly ProviderFactory _factory;
    private readonly OverlayManager _overlays;
    private readonly MicRecorder _mic = new();
    private readonly ConversationMemory _memory = new();
    private readonly string _debugFolder;
    private ITextToSpeech _tts;

    private CancellationTokenSource? _cts;
    private BitmapSource? _rawShot;
    private MonitorHandle? _monitor;
    private (string Title, string Process) _foreground;
    private Win32.POINT _cursor;
    private CancellationTokenSource? _clearTimer;

    public AssistantState State { get; private set; } = AssistantState.Idle;
    public event Action<AssistantState>? StateChanged;

    /// <summary>Friendly problems to show in a tray notification.</summary>
    public event Action<string>? Problem;

    public AssistantController(Func<AppSettings> settings, ProviderFactory factory, OverlayManager overlays, string debugFolder)
    {
        _settings = settings;
        _factory = factory;
        _overlays = overlays;
        _debugFolder = debugFolder;
        _tts = factory.CreateTextToSpeech(settings());
    }

    public IReadOnlyList<VoiceInfo> Voices => _tts.Voices;

    /// <summary>Call after Settings change (voice, rate…).</summary>
    public void ReloadSpeech()
    {
        _tts.Stop();
        (_tts as IDisposable)?.Dispose();
        _tts = _factory.CreateTextToSpeech(_settings());
    }

    // ---------------- Hotkey down ----------------

    public void BeginListening()
    {
        if (State == AssistantState.Listening) return;
        Cancel(clearScreen: true);

        try
        {
            // Screenshot FIRST, before anything on screen changes.
            _cursor = Monitors.CursorPosition();
            _monitor = Monitors.FromPoint(_cursor);
            _foreground = ForegroundApp.Get();
            _rawShot = ScreenCapture.GrabRaw(_monitor);

            _mic.Start();
            _overlays.For(_monitor).SetGlow(GlowState.Listening);
            SetState(AssistantState.Listening);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    // ---------------- Hotkey up ----------------

    public async void EndListening()
    {
        if (State != AssistantState.Listening || _monitor is null || _rawShot is null) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        var ct = cts.Token;
        var monitor = _monitor;
        var overlay = _overlays.For(monitor);
        var settings = _settings();
        var lang = LanguageProfile.For(settings.Language);

        try
        {
            var recording = await _mic.StopAsync();
            overlay.SetGlow(GlowState.Thinking);
            SetState(AssistantState.Thinking);

            var raw = _rawShot;
            var shotTask = Task.Run(() => ScreenCapture.Encode(raw, monitor, settings.MaxImageLongEdge), ct);

            var pipeline = new AskPipeline(
                _factory.CreateSpeechToText(settings, (p, e) => Log.Warning(e, "Speech provider {Provider} failed, falling back", p.Name)),
                _factory.CreateBrain(settings),
                _memory);

            var questionTask = pipeline.TranscribeAsync(recording.Wav, recording.HasSpeech, lang, ct);
            await Task.WhenAll(shotTask, questionTask);
            var shot = shotTask.Result;
            var question = questionTask.Result;
            Log.Information("Question ({Lang}, {Secs:0.0}s audio): {Question}", settings.Language, recording.Duration.TotalSeconds,
                string.IsNullOrEmpty(question) ? "(explain screen)" : question);

            if (!string.IsNullOrEmpty(question)) overlay.ShowCaption("🎤 " + question);

            var cursorInImage = shot.Mapper.PhysicalToImage(_cursor.X, _cursor.Y);
            var screen = new CapturedScreen
            {
                Image = shot.Jpeg,
                Context = new ScreenContext(_foreground.Title, _foreground.Process, shot.Mapper.Image.Width, shot.Mapper.Image.Height,
                    (int)cursorInImage.X, (int)cursorInImage.Y),
            };
            if (settings.DebugMode) SaveDebug(shot.Jpeg, question);

            var result = await pipeline.AskAsync(screen, question, lang, null, ct);
            if (settings.DebugMode) Log.Debug("Answer: {@Answer}", result.Answer);

            overlay.SetGlow(GlowState.None);
            SetState(AssistantState.Speaking);
            await PresentAsync(result.Answer, shot, overlay, ct);

            SetState(AssistantState.Idle);
            ScheduleClear(overlay, settings.ClearAfterSeconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Esc or a new question; nothing to report.
        }
        catch (Exception ex)
        {
            Fail(ex, overlay);
        }
        finally
        {
            _rawShot = null;
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>Speaks each step while its pointer is on screen.</summary>
    private async Task PresentAsync(BrainAnswer answer, ScreenShot shot, OverlayWindow overlay, CancellationToken ct)
    {
        // The window may be using a slightly different DPI than the monitor reported.
        var fix = shot.Mapper.Monitor.DpiScale / overlay.DpiScale;
        var cursorLocal = new Point(
            (_cursor.X - shot.Mapper.Monitor.Bounds.Left) / overlay.DpiScale,
            (_cursor.Y - shot.Mapper.Monitor.Bounds.Top) / overlay.DpiScale);

        for (int i = 0; i < answer.Steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = answer.Steps[i];
            var caption = answer.Steps.Count > 1 ? $"{i + 1}/{answer.Steps.Count}  {step.Say}" : step.Say;
            overlay.ShowCaption(caption);

            if (step.Point is { } p)
            {
                var c = shot.Mapper.ImageToOverlay(p.X, p.Y);
                overlay.ShowPointer(p.Type, c.X * fix, c.Y * fix,
                    shot.Mapper.ImageLengthToOverlay(p.W, true) * fix,
                    shot.Mapper.ImageLengthToOverlay(p.H, false) * fix,
                    p.Label, cursorLocal);
            }
            else
            {
                overlay.ClearPointer();
            }

            var line = VoicePicker.PlanLine(step, answer.Language, _tts.Voices);
            var spoke = await _tts.SpeakAsync(line, ct);
            if (!spoke)
            {
                Log.Information("No installed voice for {Lang}; showing caption only", line.LanguageCode);
                Problem?.Invoke(VoiceMissingMessage(line.LanguageCode));
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(step.Say.Length / 12.0, 3, 12)), ct);
            }
            else if (i < answer.Steps.Count - 1)
            {
                await Task.Delay(600, ct); // small breath between steps
            }
        }
    }

    private bool _voiceTipShown;

    private string VoiceMissingMessage(string? lang)
    {
        if (_voiceTipShown) return "";
        _voiceTipShown = true;
        var name = lang switch { "ur" => "Urdu", "hi" => "Hindi", _ => lang ?? "this language" };
        return $"No {name} voice is installed, so the answer is shown as text. Add one in Windows Settings › Time & language › Language & region › {name} › Speech, or pick Roman Urdu / Hinglish in Rahbar's language menu.";
    }

    private void ScheduleClear(OverlayWindow overlay, int seconds)
    {
        _clearTimer?.Cancel();
        var t = new CancellationTokenSource();
        _clearTimer = t;
        _ = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)), t.Token).ContinueWith(task =>
        {
            if (!task.IsCanceled) Application.Current?.Dispatcher.BeginInvoke(overlay.FadeOutAll);
        }, TaskScheduler.Default);
    }

    // ---------------- Esc / cancel ----------------

    public void Cancel(bool clearScreen = true)
    {
        _cts?.Cancel();
        _tts.Stop();
        if (_mic.IsRecording) _ = _mic.StopAsync();
        _clearTimer?.Cancel();
        if (clearScreen) _overlays.ClearAll();
        if (State != AssistantState.Idle) SetState(AssistantState.Idle);
    }

    private void Fail(Exception ex, OverlayWindow? overlay = null)
    {
        var friendly = ex as FriendlyException ?? (ex as AggregateException)?.InnerException as FriendlyException;
        if (friendly is null) Log.Error(ex, "Question failed");
        else Log.Warning("Question failed: {Message}", friendly.Message);

        var message = friendly?.Message ?? "Something went wrong. Please try again. (Details are in the log.)";
        if (overlay is not null)
        {
            overlay.SetGlow(GlowState.None);
            overlay.ClearPointer();
            overlay.ShowCaption("⚠ " + message);
            ScheduleClear(overlay, 6);
        }
        Problem?.Invoke(message);
        SetState(AssistantState.Idle);
    }

    private void SetState(AssistantState s)
    {
        State = s;
        StateChanged?.Invoke(s);
    }

    private void SaveDebug(byte[] jpeg, string question)
    {
        try
        {
            Directory.CreateDirectory(_debugFolder);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.WriteAllBytes(Path.Combine(_debugFolder, stamp + ".jpg"), jpeg);
            File.WriteAllText(Path.Combine(_debugFolder, stamp + ".txt"), question);
        }
        catch (Exception ex) { Log.Warning(ex, "Could not save debug screenshot"); }
    }

    public void Dispose()
    {
        Cancel();
        _mic.Dispose();
        (_tts as IDisposable)?.Dispose();
    }
}
