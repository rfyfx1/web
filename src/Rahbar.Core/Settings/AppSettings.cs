using System.Text.Json;
using System.Text.Json.Serialization;
using Rahbar.Core.Language;

namespace Rahbar.Core.Settings;

public static class ProviderIds
{
    public const string Gemini = "gemini";
    public const string Claude = "claude";
    public const string Ollama = "ollama";
    public const string Groq = "groq";
    public const string WindowsSpeech = "windows";
}

/// <summary>Names used for API keys in the secret store (never in settings.json).</summary>
public static class SecretNames
{
    public const string Gemini = "Rahbar/GeminiApiKey";
    public const string Claude = "Rahbar/ClaudeApiKey";
    public const string Groq = "Rahbar/GroqApiKey";
}

public sealed class HotkeySettings
{
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; }
    public bool Alt { get; set; }
    public bool Win { get; set; }

    /// <summary>Windows virtual-key code. 0x20 = Space.</summary>
    public int Key { get; set; } = 0x20;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    public static string KeyName(int vk) => vk switch
    {
        0x20 => "Space",
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0xC0 => "`",
        _ => $"Key {vk}",
    };
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public bool FirstRunCompleted { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LanguagePreference Language { get; set; } = LanguagePreference.Auto;

    public string BrainProvider { get; set; } = ProviderIds.Gemini;
    public string SpeechToTextProvider { get; set; } = ProviderIds.Groq;

    /// <summary>Forces Ollama + offline Windows recognition so nothing leaves the PC.</summary>
    public bool PrivateMode { get; set; }

    public bool Paused { get; set; }

    // Model names change often; they are settings, not code.
    public string GeminiModel { get; set; } = "gemini-flash-latest";
    public string ClaudeModel { get; set; } = "claude-sonnet-5-5";
    public string OllamaModel { get; set; } = "qwen2.5vl:7b";
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string GroqModel { get; set; } = "whisper-large-v3-turbo";

    public HotkeySettings Hotkey { get; set; } = new();

    /// <summary>Tap to type a question instead of speaking (quiet offices).</summary>
    public HotkeySettings TypedHotkey { get; set; } = new() { Ctrl = true, Shift = true, Key = 0x20 };

    /// <summary>Hold to dictate: whatever you say is typed into the focused text box.</summary>
    public HotkeySettings DictationHotkey { get; set; } = new() { Ctrl = true, Alt = true, Key = 0x20 };

    /// <summary>Name of the active skill file (without .md), or null for none.</summary>
    public string? ActiveSkill { get; set; }

    /// <summary>Clicking the highlighted target moves a walkthrough to the next step.</summary>
    public bool AdvanceOnTargetClick { get; set; } = true;

    /// <summary>Take a fresh screenshot for each walkthrough step so pointers match the changed screen.</summary>
    public bool FreshScreenshotPerStep { get; set; } = true;

    /// <summary>Where to look for updates (e.g. https://github.com/org/repo). Empty = no update checks.</summary>
    public string UpdateUrl { get; set; } = "";

    /// <summary>Empty = pick a voice automatically for each reply's language.</summary>
    public string? PreferredVoice { get; set; }

    /// <summary>Speech rate, 0.5 – 2.0.</summary>
    public double SpeechRate { get; set; } = 1.0;

    public int MaxImageLongEdge { get; set; } = 1568;

    /// <summary>Seconds the pointer stays after speaking finishes.</summary>
    public int ClearAfterSeconds { get; set; } = 8;

    /// <summary>Debug only: saves screenshots and raw replies next to the logs.</summary>
    public bool DebugMode { get; set; }

    public string EffectiveBrain => PrivateMode ? ProviderIds.Ollama : BrainProvider;
    public string EffectiveSpeechToText => PrivateMode ? ProviderIds.WindowsSpeech : SpeechToTextProvider;
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public string FilePath { get; }

    public SettingsStore(string filePath) => FilePath = filePath;

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rahbar");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A broken file shouldn't stop the app; keep a copy and start fresh.
            File.Copy(FilePath, FilePath + ".broken", overwrite: true);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
