# Rahbar: a screen-aware voice helper for Windows

Hold a hotkey, ask a question about what's on your screen, and let go. Rahbar looks at your
screen, **answers out loud**, and **draws a circle on the exact button** you need.

Talk to it in **any language, including mixed speech**: English, Urdu (اردو), Roman Urdu,
Hindi (हिन्दी), Hinglish, Punjabi and others. Rahbar answers in the same language and style you used.

> "Rahbar" (رہبر) is Urdu for "guide".

---

## Install (for the team)

1. Download **Rahbar-win-x64** from the latest successful run on the repo's **Actions** tab
   (*Build Rahbar* → open the run → *Artifacts*). Unzip it to get `Rahbar.exe`.
2. Put `Rahbar.exe` somewhere permanent, e.g. `C:\Users\<you>\Apps\Rahbar\`, and double-click it.
   - Windows may say *"Windows protected your PC"* because the app isn't code-signed yet.
     Click **More info → Run anyway**.
3. The setup wizard opens (about 2 minutes):
   1. **Language**: keep **Auto** unless you always want one style.
   2. **Gemini key** (free): open <https://aistudio.google.com/apikey>, sign in, click
      **Create API key**, copy it, paste it, then press **Test**.
   3. **Groq key** (free): open <https://console.groq.com/keys>, sign in, click
      **Create API Key**, copy it, paste it, then press **Test**.
   4. **Mic test**: press Start and say something, e.g. *"Save ka button kahan hai?"*
   5. **Hotkey**: keep **Ctrl + Space** or pick another.
4. Rahbar now lives in the tray near the clock (teal circle icon).

Each person uses **their own** free keys, so free limits apply per person. Keys are stored in
**Windows Credential Manager**, never in files or logs.

To start Rahbar with Windows: press `Win + R`, type `shell:startup`, and put a shortcut to `Rahbar.exe` there.

## How to use

| Action | What happens |
|---|---|
| **Hold Ctrl + Space**, speak, let go | Rahbar answers your question and points at the right spot |
| **Tap Ctrl + Space** (don't speak) | Rahbar explains what's on the screen |
| **Esc** | Stops talking and clears the screen |
| Right-click the tray icon | Settings, Language, Private mode, Pause, Quit |

While Rahbar listens, the screen edge **glows red** and the tray icon turns red. While it
thinks, both turn amber. Rahbar takes a screenshot **only** when you press the hotkey, and
only of the monitor your mouse is on.

Examples:
- "Where is the save button?"
- "Is mein naya column kaise add karun?"
- "ये बटन क्या करता है?"
- "Seller Central mein inventory kahan dekhte hain?"

## Languages

| Setting | You speak | Rahbar answers |
|---|---|---|
| **Auto** (default) | Anything, mixed is fine | Same language, script and mix as you |
| English | English | English |
| اردو Urdu | Urdu | Urdu script |
| Roman Urdu | Urdu / mixed | Urdu in English letters: *"Upar left mein File pe click karein"* |
| हिन्दी Hindi | Hindi | Devanagari |
| Hinglish | Hindi / mixed | Hindi + English in English letters: *"Top left pe File menu kholo"* |

Button and menu names stay in English exactly as they appear on screen, so you can find them.

**Voices.** Rahbar uses the voices built into Windows and picks one per answer:
- Roman Urdu and Hinglish answers are spoken with a native **Urdu or Hindi voice** if one is
  installed. Otherwise an Indian-English voice reads them.
- To add Urdu or Hindi voices: **Windows Settings → Time & language → Language & region →
  Add a language** (Urdu or Hindi), then tick **Text-to-speech / Speech**.
- If no voice can read an answer, it still appears as a caption at the bottom of the screen.

## Privacy

- Free-tier AI providers (Gemini, Groq) **may use your requests to improve their models**.
  Don't use free mode on screens with private data (passwords, bank details, customer info).
- **Private mode** (tray menu) switches to a local **Ollama** model and offline Windows speech
  recognition, so nothing leaves the PC. It needs Ollama installed (<https://ollama.com>), then
  `ollama pull qwen2.5vl:7b` and a decent GPU. Offline recognition understands English only.
- Screenshots aren't saved unless you turn on **Debug mode** in Settings.

## Troubleshooting

| Problem | Fix |
|---|---|
| "Free limit reached, try again in a minute" | The free tier has per-minute limits. Wait a minute. |
| "The API key was rejected" | Settings → paste the key again → **Test**. |
| Gemini **Test** says the model isn't found | Google renames models often. In Settings, change *Model* to a current Flash model listed at <https://ai.google.dev/gemini-api/docs/models>. |
| Nothing happens when I hold the hotkey | Check the tray icon isn't grey (paused). Another app may use the same keys, so pick a different hotkey in Settings. |
| It doesn't hear me | Windows Settings → System → Sound → Input: choose the right mic. Re-run the mic test in Settings. |
| The circle is in the wrong place | Turn on Debug mode, try again, and share the files in the log folder (tray → *Open log folder*). |
| The answer is in the wrong language | Pick a fixed language in the tray **Language** menu instead of Auto. |
| The answer is text only, no sound | No voice is installed for that language. See *Voices* above. |

Logs: `%AppData%\Rahbar\logs`. Settings: `%AppData%\Rahbar\settings.json`.

---

## For developers

```
Rahbar.sln
src/Rahbar.Core          pipeline, interfaces, prompt, JSON parsing, coordinate mapping, languages (cross-platform)
src/Rahbar.Providers     Gemini, Claude, Ollama, Groq Whisper, Windows voices and recognition
src/Rahbar.App           WPF tray app: hotkey hook, capture, mic, overlay, settings, setup wizard
tests/Rahbar.Core.Tests  xUnit tests
docs/PROMPT.md           the full product spec and phases
```

```powershell
dotnet build Rahbar.sln
dotnet test tests/Rahbar.Core.Tests
dotnet run --project src/Rahbar.App
# single exe:
dotnet publish src/Rahbar.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Requires the .NET 8 SDK on Windows 10 (1809+) or 11. The Core tests also run on Linux and macOS.

Key design points:
- Every AI provider sits behind `IVisionBrain`, `ISpeechToText` or `ITextToSpeech`
  (`src/Rahbar.Core/Abstractions`). `ProviderFactory` picks providers from settings.
- Model names (`gemini-flash-latest`, `whisper-large-v3-turbo`, `claude-sonnet-5-5`,
  `qwen2.5vl:7b`) are settings, not code.
- Coordinates: the model answers in screenshot pixels. `CoordinateMapper` converts them to
  physical screen pixels and to overlay DIPs, handling downscaling, 100–200% DPI and monitors
  at negative coordinates. All of this is unit tested.
- Language: `LanguageProfile` sets the Whisper language hint, a style prompt that steers
  Whisper toward Roman script for Roman Urdu and Hinglish, and the reply rule in the brain
  prompt. The brain can return a native-script `speak` line next to a romanized `say` line,
  so a native voice can pronounce it. `VoicePicker` chooses the voice.

### Status

**Phase 1 is built:** tray app, hold-to-talk hotkey, screenshot of the current monitor, Groq
transcription (with offline fallback), Gemini/Claude/Ollama brains, Windows voices, pointer
overlay, Esc to cancel, settings with key **Test**, first-run wizard, 429 retry and backoff.

Already in place from Phase 2: arrow and box spotlight pointers, caption bubble, multi-step
answers (each step's pointer shows while it is spoken), Private-mode toggle, any-language and
mixed-language support.

Still to do in Phase 2: Next/✕ step controls and saying "next" / "agla" to advance, typed
questions (Ctrl+Shift+Space), history window, skills folder, dictation, usage counter, and a
Velopack installer. Phase 1 uses GDI capture; Windows.Graphics.Capture can be added later.
