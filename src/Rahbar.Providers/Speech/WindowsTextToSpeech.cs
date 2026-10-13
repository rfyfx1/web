using Rahbar.Core.Abstractions;
using Rahbar.Core.Language;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Rahbar.Providers.Speech;

/// <summary>Built-in Windows voices (free, offline). Picks a voice per reply language and script.</summary>
public sealed class WindowsTextToSpeech : ITextToSpeech, IDisposable
{
    private readonly SpeechSynthesizer _synth = new();
    private readonly MediaPlayer _player = new() { AutoPlay = false };
    private readonly object _gate = new();
    private TaskCompletionSource? _current;

    public WindowsTextToSpeech(string? preferredVoice, double rate)
    {
        PreferredVoice = preferredVoice;
        _synth.Options.SpeakingRate = Math.Clamp(rate, 0.5, 2.0);
        Voices = SpeechSynthesizer.AllVoices.Select(v => new VoiceInfo(v.DisplayName, v.Language)).ToList();
        _player.MediaEnded += (_, _) => Complete();
        _player.MediaFailed += (_, _) => Complete();
    }

    public string Name => "Windows voices";
    public string? PreferredVoice { get; }
    public IReadOnlyList<VoiceInfo> Voices { get; }

    public static IReadOnlyList<VoiceInfo> InstalledVoices() =>
        SpeechSynthesizer.AllVoices.Select(v => new VoiceInfo(v.DisplayName, v.Language)).ToList();

    public async Task<bool> SpeakAsync(SpeechLine line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line.Text)) return true;
        var pick = VoicePicker.Pick(line, Voices, PreferredVoice);
        if (pick is null) return false;

        _synth.Voice = SpeechSynthesizer.AllVoices.First(v => v.DisplayName == pick.Name);
        var stream = await _synth.SynthesizeTextToStreamAsync(line.Text).AsTask(ct).ConfigureAwait(false);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _current?.TrySetResult();
            _current = tcs;
            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }

        using (ct.Register(Stop))
            await tcs.Task.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return true;
    }

    public void Stop()
    {
        lock (_gate)
        {
            try { _player.Pause(); } catch { /* already stopped */ }
            _player.Source = null;
            _current?.TrySetResult();
            _current = null;
        }
    }

    private void Complete()
    {
        lock (_gate)
        {
            _current?.TrySetResult();
            _current = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _player.Dispose();
        _synth.Dispose();
    }
}
