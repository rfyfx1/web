using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Rahbar.App.Audio;
using Rahbar.App.Capture;
using Rahbar.App.Input;
using Rahbar.App.Native;
using Rahbar.App.Overlay;
using Rahbar.App.Windows;
using Rahbar.Core.Abstractions;
using Rahbar.Core.Geometry;
using Rahbar.Core.Language;
using Rahbar.Core.Pipeline;
using Rahbar.Core.Prompts;
using Rahbar.Core.Settings;
using Rahbar.Core.Storage;
using Rahbar.Core.Walkthrough;
using Rahbar.Providers;
using Serilog;

namespace Rahbar.App.Services;

public enum AssistantState { Idle, Listening, Thinking, Speaking, WaitingForNext, Dictating }

/// <summary>Everything grabbed the instant a hotkey goes down, before the screen changes.</summary>
internal sealed record Capture(BitmapSource Raw, MonitorHandle Monitor, string WindowTitle, string Process, Win32.POINT Cursor);

/// <summary>
/// Runs Rahbar's main flows:
///  • Ask: hotkey down → screenshot + mic → hotkey up → transcribe → AI → speak + point.
///  • Walkthroughs: one step at a time; "next"/"agla", the Next pill or clicking the target moves on,
///    and each new step is located on a fresh screenshot.
///  • Typed questions and dictation.
/// Esc stops everything.
/// </summary>
internal sealed class AssistantController : IDisposable
{
    private readonly Func<AppSettings> _settings;
    private readonly ProviderFactory _factory;
    private readonly OverlayManager _overlays;
    private readonly HistoryStore _history;
    private readonly UsageCounter _usage;
    private readonly SkillStore _skills;
    private readonly string _debugFolder;
    private readonly MicRecorder _mic = new();
    private readonly ConversationMemory _memory = new();
    private readonly StepPillWindow _pill = new();
    private readonly MouseClickHook _clicks;
    private ITextToSpeech _tts;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _clearTimer;
    private Capture? _pending;
    private HotkeyId? _listening;

    // Walkthrough state
    private WalkthroughSession? _session;
    private readonly Dictionary<int, CoordinateMapper> _stepMappers = new();
    private MonitorHandle? _sessionMonitor;
    private Win32.POINT _sessionCursor;
    private bool _busy;

    public AssistantState State { get; private set; } = AssistantState.Idle;
    public event Action<AssistantState>? StateChanged;

    /// <summary>Friendly problems to show in a tray notification.</summary>
    public event Action<string>? Problem;

    public AssistantController(Func<AppSettings> settings, ProviderFactory factory, OverlayManager overlays,
        HistoryStore history, UsageCounter usage, SkillStore skills, string debugFolder)
    {
        _settings = settings;
        _factory = factory;
        _overlays = overlays;
        _history = history;
        _usage = usage;
        _skills = skills;
        _debugFolder = debugFolder;
        _tts = factory.CreateTextToSpeech(settings());

        _clicks = new MouseClickHook(Application.Current.Dispatcher);
        _clicks.LeftClick += OnLeftClick;
        _pill.NextClicked += () => Next(null);
        _pill.BackClicked += Back;
        _pill.CloseClicked += () => Cancel();
    }

    /// <summary>Call after Settings change (voice, rate…).</summary>
    public void ReloadSpeech()
    {
        _tts.Stop();
        (_tts as IDisposable)?.Dispose();
        _tts = _factory.CreateTextToSpeech(_settings());
    }

    // =====================================================================
    // Hold-to-talk
    // =====================================================================

    public void BeginListening()
    {
        if (_listening is not null) return;
        StopCurrentWork(); // keeps an open walkthrough so "next" can continue it
        try
        {
            _pending = CaptureNow();
            _mic.Start();
            _listening = HotkeyId.Ask;
            _overlays.For(_pending.Monitor).SetGlow(GlowState.Listening);
            SetState(AssistantState.Listening);
        }
        catch (Exception ex)
        {
            _listening = null;
            Fail(ex, null);
        }
    }

    public void EndListening()
    {
        if (_listening != HotkeyId.Ask || _pending is null) return;
        _listening = null;
        var capture = _pending;
        _pending = null;

        Run(async ct =>
        {
            var overlay = _overlays.For(capture.Monitor);
            var recording = await _mic.StopAsync();
            overlay.SetGlow(GlowState.Thinking);
            SetState(AssistantState.Thinking);

            var settings = _settings();
            var lang = LanguageProfile.For(settings.Language);
            var pipeline = CreatePipeline(settings);
            var question = await pipeline.TranscribeAsync(recording.Wav, recording.HasSpeech, lang, ct);
            Log.Information("Heard ({Lang}, {Secs:0.0}s): {Question}", settings.Language, recording.Duration.TotalSeconds,
                string.IsNullOrEmpty(question) ? "(nothing)" : question);

            await HandleQuestionAsync(capture, question, pipeline, ct);
        }, capture.Monitor);
    }

    // =====================================================================
    // Typed question (Ctrl+Shift+Space)
    // =====================================================================

    public void AskTyped()
    {
        if (_listening is not null) return;
        StopCurrentWork();
        Capture capture;
        try { capture = CaptureNow(); }
        catch (Exception ex) { Fail(ex, null); return; }

        var box = new TypedQuestionWindow(capture.Cursor.X, capture.Cursor.Y, capture.Monitor.Info.Bounds);
        box.Closed += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Question)) return;
            Run(async ct =>
            {
                _overlays.For(capture.Monitor).SetGlow(GlowState.Thinking);
                SetState(AssistantState.Thinking);
                await HandleQuestionAsync(capture, box.Question!, CreatePipeline(_settings()), ct);
            }, capture.Monitor);
        };
        box.Show();
    }

    // =====================================================================
    // Questions, walkthrough commands
    // =====================================================================

    private async Task HandleQuestionAsync(Capture capture, string question, AskPipeline pipeline, CancellationToken ct)
    {
        // During a walkthrough, short commands ("next", "agla", "bas"…) drive it instead of asking the AI.
        if (_session is { IsMultiStep: true })
        {
            switch (StepCommands.Parse(question))
            {
                case StepCommand.Next:
                    await GoNextAsync(capture, ct);
                    return;
                case StepCommand.Back:
                    _session.MoveBack(); // on step 1 this just repeats it
                    await PresentAsync(ct);
                    return;
                case StepCommand.Repeat:
                    await PresentAsync(ct);
                    return;
                case StepCommand.Stop:
                    EndSession(clear: true);
                    SetState(AssistantState.Idle);
                    return;
            }
        }

        EndSession(clear: false);
        var overlay = _overlays.For(capture.Monitor);
        overlay.ClearPointer();
        if (!string.IsNullOrEmpty(question)) overlay.ShowCaption("🎤 " + question);

        var settings = _settings();
        var shot = await Task.Run(() => ScreenCapture.Encode(capture.Raw, capture.Monitor, settings.MaxImageLongEdge), ct);
        var screen = ToScreen(capture, shot);
        if (settings.DebugMode) SaveDebug(shot.Jpeg, question);

        var result = await pipeline.AskAsync(screen, question, LanguageProfile.For(settings.Language), ActiveSkillText(settings), ct);
        if (settings.DebugMode) Log.Debug("Answer: {@Answer}", result.Answer);

        _history.Add(new HistoryEntry(DateTime.Now, result.WasExplainScreen ? "(explain this screen)" : question,
            result.Answer.FullText, result.Answer.Language, capture.Process));

        _session = new WalkthroughSession(question, result.Answer);
        _stepMappers.Clear();
        for (int i = 0; i < _session.Count; i++) _stepMappers[i] = shot.Mapper;
        _sessionMonitor = capture.Monitor;
        _sessionCursor = capture.Cursor;

        await PresentAsync(ct);
    }

    /// <summary>Moves to the next step (from the pill, a click on the target, or a voice command).</summary>
    public void Next(Capture? capture)
    {
        if (_session is null || _busy) return;
        StopCurrentWork();
        Run(ct => GoNextAsync(capture, ct), _sessionMonitor);
    }

    public void Back()
    {
        if (_session is null || _busy || !_session.MoveBack()) return;
        StopCurrentWork();
        Run(PresentAsync, _sessionMonitor);
    }

    private async Task GoNextAsync(Capture? capture, CancellationToken ct)
    {
        var session = _session;
        if (session is null || _sessionMonitor is null) return;
        if (!session.MoveNext())
        {
            EndSession(clear: true); // "Done ✓" on the last step
            SetState(AssistantState.Idle);
            return;
        }

        var settings = _settings();
        if (!settings.FreshScreenshotPerStep || session.CurrentIsFresh)
        {
            await PresentAsync(ct);
            return;
        }

        _busy = true;
        try
        {
            var overlay = _overlays.For(_sessionMonitor);
            overlay.ClearPointer();
            overlay.SetGlow(GlowState.Thinking);
            _pill.ShowStep(_sessionMonitor, session.Index, session.Count, busy: true);
            SetState(AssistantState.Thinking);

            if (capture is null)
            {
                await Task.Delay(400, ct); // let the app finish reacting to the user's click
                capture = CaptureNow(_sessionMonitor);
            }
            var shot = await Task.Run(() => ScreenCapture.Encode(capture.Raw, capture.Monitor, settings.MaxImageLongEdge), ct);
            try
            {
                var step = await CreatePipeline(settings).ContinueAsync(ToScreen(capture, shot), session.Question, session.Planned,
                    session.Index, LanguageProfile.For(settings.Language), ActiveSkillText(settings), ct);
                session.ReplaceCurrent(step);
                _stepMappers[session.Index] = shot.Mapper;
                _sessionCursor = capture.Cursor;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Fall back to the original plan rather than stopping the walkthrough.
                Log.Warning(ex, "Couldn't re-locate step {Step}; using the original plan", session.Index + 1);
            }
        }
        finally
        {
            _busy = false;
        }
        await PresentAsync(ct);
    }

    /// <summary>Shows and speaks the current step of the walkthrough.</summary>
    private async Task PresentAsync(CancellationToken ct)
    {
        var session = _session;
        var monitor = _sessionMonitor;
        if (session is null || monitor is null) return;

        var overlay = _overlays.For(monitor);
        overlay.SetGlow(GlowState.None);
        var step = session.Current;
        var mapper = _stepMappers[session.Index];
        overlay.ShowCaption(session.IsMultiStep ? $"{session.Progress}  {step.Say}" : step.Say);

        if (step.Point is { } p)
        {
            // The window may be using a slightly different DPI than the monitor reported.
            var fix = mapper.Monitor.DpiScale / overlay.DpiScale;
            var c = mapper.ImageToOverlay(p.X, p.Y);
            var cursor = new Point((_sessionCursor.X - mapper.Monitor.Bounds.Left) / overlay.DpiScale,
                                   (_sessionCursor.Y - mapper.Monitor.Bounds.Top) / overlay.DpiScale);
            overlay.ShowPointer(p.Type, c.X * fix, c.Y * fix,
                mapper.ImageLengthToOverlay(p.W, true) * fix, mapper.ImageLengthToOverlay(p.H, false) * fix, p.Label, cursor);
        }
        else
        {
            overlay.ClearPointer();
        }

        if (session.IsMultiStep)
        {
            _pill.ShowStep(monitor, session.Index, session.Count, busy: false);
            if (_settings().AdvanceOnTargetClick && step.Point is not null) _clicks.Start(); else _clicks.Stop();
        }

        SetState(AssistantState.Speaking);
        var line = VoicePicker.PlanLine(step, session.Language, _tts.Voices);
        if (!await _tts.SpeakAsync(line, ct))
        {
            Log.Information("No installed voice for {Lang}; showing caption only", line.LanguageCode);
            Problem?.Invoke(VoiceMissingMessage(line.LanguageCode));
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(step.Say.Length / 12.0, 3, 12)), ct);
        }

        if (!session.IsMultiStep)
        {
            SetState(AssistantState.Idle);
            ScheduleClear(_settings().ClearAfterSeconds, endSession: true);
        }
        else if (session.IsLast)
        {
            SetState(AssistantState.WaitingForNext);
            ScheduleClear(_settings().ClearAfterSeconds, endSession: true);
        }
        else
        {
            SetState(AssistantState.WaitingForNext);
            ScheduleClear(300, endSession: true); // tidy up if the walkthrough is abandoned
        }
    }

    private void OnLeftClick(int x, int y)
    {
        var session = _session;
        if (session is null || _busy || State != AssistantState.WaitingForNext && State != AssistantState.Speaking) return;
        if (session.Current.Point is not { } p || !TargetHitTest.IsHit(p, _stepMappers[session.Index], x, y)) return;
        Log.Information("Target clicked; moving to the next step");
        Next(null);
    }

    // =====================================================================
    // Dictation (hold Ctrl+Alt+Space)
    // =====================================================================

    public void BeginDictation()
    {
        if (_listening is not null) return;
        StopCurrentWork();
        try
        {
            var monitor = Monitors.UnderCursor();
            _mic.Start();
            _listening = HotkeyId.Dictate;
            _pending = null;
            var overlay = _overlays.For(monitor);
            overlay.SetGlow(GlowState.Listening);
            overlay.ShowCaption("✍ Dictating… let go to type it");
            _dictationMonitor = monitor;
            SetState(AssistantState.Dictating);
        }
        catch (Exception ex)
        {
            _listening = null;
            Fail(ex, null);
        }
    }

    private MonitorHandle? _dictationMonitor;

    public void EndDictation()
    {
        if (_listening != HotkeyId.Dictate || _dictationMonitor is null) return;
        _listening = null;
        var monitor = _dictationMonitor;

        Run(async ct =>
        {
            var overlay = _overlays.For(monitor);
            var recording = await _mic.StopAsync();
            overlay.SetGlow(GlowState.Thinking);
            SetState(AssistantState.Thinking);

            var settings = _settings();
            var text = await CreatePipeline(settings).TranscribeAsync(recording.Wav, recording.HasSpeech, LanguageProfile.For(settings.Language), ct);
            overlay.SetGlow(GlowState.None);
            overlay.HideCaption();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetState(AssistantState.Idle);
                return;
            }
            Log.Information("Dictated {Chars} characters", text.Length);
            await TextTyper.TypeAsync(text, ct);
            SetState(AssistantState.Idle);
        }, monitor);
    }

    // =====================================================================
    // Plumbing
    // =====================================================================

    /// <summary>Runs an async flow with cancellation and friendly error handling.</summary>
    private async void Run(Func<CancellationToken, Task> work, MonitorHandle? monitor)
    {
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Esc, or a newer action took over.
        }
        catch (Exception ex)
        {
            Fail(ex, monitor is null ? null : _overlays.For(monitor));
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts.Dispose();
        }
    }

    private AskPipeline CreatePipeline(AppSettings settings)
    {
        var p = new AskPipeline(
            _factory.CreateSpeechToText(settings, (prov, e) => Log.Warning(e, "Speech provider {Provider} failed, falling back", prov.Name)),
            _factory.CreateBrain(settings),
            _memory);
        p.RequestMade += _usage.Increment;
        return p;
    }

    private static Capture CaptureNow(MonitorHandle? monitor = null)
    {
        var cursor = Monitors.CursorPosition();
        var m = monitor ?? Monitors.FromPoint(cursor);
        var (title, process) = ForegroundApp.Get();
        return new Capture(ScreenCapture.GrabRaw(m), m, title, process, cursor);
    }

    private static CapturedScreen ToScreen(Capture c, ScreenShot shot)
    {
        var cur = shot.Mapper.PhysicalToImage(c.Cursor.X, c.Cursor.Y);
        return new CapturedScreen
        {
            Image = shot.Jpeg,
            Context = new ScreenContext(c.WindowTitle, c.Process, shot.Mapper.Image.Width, shot.Mapper.Image.Height, (int)cur.X, (int)cur.Y),
        };
    }

    private string? ActiveSkillText(AppSettings s)
    {
        try { return _skills.Read(s.ActiveSkill); }
        catch (Exception ex) { Log.Warning(ex, "Couldn't read skill {Skill}", s.ActiveSkill); return null; }
    }

    private bool _voiceTipShown;

    private string VoiceMissingMessage(string? lang)
    {
        if (_voiceTipShown) return "";
        _voiceTipShown = true;
        var name = lang switch { "ur" => "Urdu", "hi" => "Hindi", _ => lang ?? "this language" };
        return $"No {name} voice is installed, so the answer is shown as text. Add one in Windows Settings › Time & language › Language & region › {name} › Speech, or pick Roman Urdu / Hinglish in Rahbar's language menu.";
    }

    private void ScheduleClear(int seconds, bool endSession)
    {
        _clearTimer?.Cancel();
        var t = new CancellationTokenSource();
        _clearTimer = t;
        _ = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)), t.Token).ContinueWith(task =>
        {
            if (task.IsCanceled) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (t.IsCancellationRequested) return;
                if (endSession) EndSession(clear: true);
                else _overlays.ClearAll();
                if (State is AssistantState.WaitingForNext) SetState(AssistantState.Idle);
            });
        }, TaskScheduler.Default);
    }

    /// <summary>Stops speech and in-flight requests but keeps an open walkthrough.</summary>
    private void StopCurrentWork()
    {
        _cts?.Cancel();
        _tts.Stop();
        _clearTimer?.Cancel();
    }

    private void EndSession(bool clear)
    {
        _session = null;
        _stepMappers.Clear();
        _clicks.Stop();
        _pill.Hide();
        if (clear) _overlays.ClearAll();
    }

    /// <summary>Esc: stop everything and clear the screen.</summary>
    public void Cancel()
    {
        StopCurrentWork();
        if (_mic.IsRecording) _ = _mic.StopAsync();
        _listening = null;
        _pending = null;
        EndSession(clear: true);
        if (State != AssistantState.Idle) SetState(AssistantState.Idle);
    }

    private void Fail(Exception ex, OverlayWindow? overlay)
    {
        var friendly = ex as FriendlyException ?? (ex as AggregateException)?.InnerException as FriendlyException;
        if (friendly is null) Log.Error(ex, "Request failed");
        else Log.Warning("Request failed: {Message}", friendly.Message);

        var message = friendly?.Message ?? "Something went wrong. Please try again. (Details are in the log.)";
        EndSession(clear: false);
        if (overlay is not null)
        {
            overlay.SetGlow(GlowState.None);
            overlay.ClearPointer();
            overlay.ShowCaption("⚠ " + message);
            ScheduleClear(6, endSession: false);
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
        _clicks.Dispose();
        _mic.Dispose();
        _pill.Close();
        (_tts as IDisposable)?.Dispose();
    }
}
