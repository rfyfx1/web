using System.IO;
using NAudio.Wave;

namespace Rahbar.App.Audio;

internal sealed record Recording(byte[] Wav, TimeSpan Duration, float Peak)
{
    /// <summary>A tap shorter than 0.4 s, or near-silence, counts as "no question".</summary>
    public bool HasSpeech => Duration >= TimeSpan.FromSeconds(0.4) && Peak >= 0.02f;
}

/// <summary>Records the default microphone as 16 kHz mono 16-bit WAV (what speech models want).</summary>
internal sealed class MicRecorder : IDisposable
{
    private static readonly WaveFormat Format = new(16000, 16, 1);
    private WaveInEvent? _wave;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<Recording>? _stopped;
    private DateTime _startedAt;
    private float _peak;

    /// <summary>Live input level 0..1 (for the mic test in setup).</summary>
    public event Action<float>? Level;

    public bool IsRecording => _wave is not null;

    public static bool HasMicrophone => WaveInEvent.DeviceCount > 0;

    public void Start()
    {
        if (_wave is not null) return;
        if (!HasMicrophone) throw new Core.Abstractions.FriendlyException("No microphone found. Plug one in and try again.");

        _peak = 0;
        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(_buffer), Format);
        _stopped = new TaskCompletionSource<Recording>(TaskCreationOptions.RunContinuationsAsynchronously);
        _wave = new WaveInEvent { WaveFormat = Format, BufferMilliseconds = 50 };
        _wave.DataAvailable += OnData;
        _wave.RecordingStopped += OnStopped;
        _startedAt = DateTime.UtcNow;
        _wave.StartRecording();
    }

    public Task<Recording> StopAsync()
    {
        var wave = _wave;
        var done = _stopped;
        if (wave is null || done is null) return Task.FromResult(new Recording(Array.Empty<byte>(), TimeSpan.Zero, 0));
        wave.StopRecording(); // RecordingStopped fires once buffers are flushed
        return done.Task;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        float max = 0;
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
            if (sample > max) max = sample;
        }
        if (max > _peak) _peak = max;
        Level?.Invoke(max);
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        var duration = DateTime.UtcNow - _startedAt;
        _writer?.Dispose(); // finalises the WAV header
        var bytes = _buffer?.ToArray() ?? Array.Empty<byte>();
        var done = _stopped;
        Cleanup();
        if (e.Exception is not null) done?.TrySetException(e.Exception);
        else done?.TrySetResult(new Recording(bytes, duration, _peak));
    }

    private void Cleanup()
    {
        if (_wave is not null)
        {
            _wave.DataAvailable -= OnData;
            _wave.RecordingStopped -= OnStopped;
            _wave.Dispose();
        }
        _wave = null;
        _writer = null;
        _buffer = null;
        _stopped = null;
    }

    public void Dispose()
    {
        try { _wave?.StopRecording(); } catch { /* ignore */ }
        Cleanup();
    }
}
