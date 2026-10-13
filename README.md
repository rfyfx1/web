# Rahbar: a screen-aware voice helper for Windows

Hold a hotkey, ask a question about what's on your screen, and let go. Rahbar looks at your
screen, **answers out loud**, and **draws a circle on the exact button** you need.

Talk to it in **any language, including mixed speech**: English, Urdu (اردو), Roman Urdu,
Hindi (हिन्दी), Hinglish, Punjabi and others. Rahbar answers in the same language and style you used.

> "Rahbar" (رہبر) is Urdu for "guide".

---

## Install (for the team)

1. Download **Rahbar-…-installer** from the latest successful run on the repo's **Actions** tab
   (*Build Rahbar* → open the run → *Artifacts*) and unzip it.
2. Double-click **`Rahbar-win-Setup.exe`**. It installs for your user only (no admin needed),
   adds a Start-menu shortcut and starts Rahbar.
   - Windows may say *"Windows protected your PC"* because the app isn't code-signed yet.
     Click **More info → Run anyway**.
   - No install rights? Use `Rahbar-win-Portable.zip` instead: unzip it and run `Rahbar.exe`.
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

To start Rahbar with Windows: press `Win + R`, type `shell:startup`, and put a copy of the
Rahbar Start-menu shortcut there.

**Updates:** an admin publishes the *update-feed* artifact (the whole `releases` folder) to a
shared folder or GitHub Releases, and puts that address in **Settings → Updates**. After that,
everyone uses tray → **Check for updates**.

## How to use

| Action | What happens |
|---|---|
| **Hold Ctrl + Space**, speak, let go | Rahbar answers your question and points at the right spot |
| **Tap Ctrl + Space** (don't speak) | Rahbar explains what's on the screen |
| **Ctrl + Shift + Space** | Type a question instead of speaking (quiet offices) |
| **Hold Ctrl + Alt + Space**, speak, let go | Dictation: Rahbar types what you said into the text box you're in, in any language |
| **Esc** | Stops talking and clears the screen |
| Click the tray icon | Today's usage, History, Language, Skill, Private mode, Settings, Pause, Quit |

### Step-by-step help (walkthroughs)

For "how do I…" questions, Rahbar answers one step at a time. A small bar at the bottom shows
**‹ Step 2/4 · Next ▸ · ✕**. To go on, do any of these:
- click the highlighted button (Rahbar notices and moves on),
- click **Next** on the bar,
- hold the hotkey and say **"next"**, **"agla"**, **"ho gaya"**, **"اگلا"** or **"अगला"**.

Say **"back" / "pichla"** to go back, **"dobara"** to repeat, and **"bas" / "stop"** to end.
For each new step Rahbar looks at the screen again, so the pointer matches what changed.

### Skills

A skill is a text file with extra instructions for Rahbar, like a guide to Seller Central or
your brand voice. Choose one from tray → **Skill**. To add your own, use **Skill → Open skills
folder…** and drop in a `.md` file written in plain language. Two examples are included.

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
| Dictation types strange characters | Let go of all keys after speaking. Rahbar waits for Ctrl/Alt to be released before typing. |
| Walkthrough doesn't move on when I click | Click right on the highlighted spot, or say "next". You can turn click-to-advance off in Settings. |

Logs: `%AppData%\Rahbar\logs`. Settings: `%AppData%\Rahbar\settings.json`.
History (text only, last 50): `%AppData%\Rahbar\history.json`. Skills: `%AppData%\Rahbar\skills`.

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

**Phase 1 (done):** tray app, hold-to-talk hotkey, screenshot of the current monitor, Groq
transcription (with offline fallback), Gemini/Claude/Ollama brains, Windows voices, pointer
overlay, Esc to cancel, settings with key **Test**, first-run wizard, 429 retry and backoff.

**Phase 2 (built):**
- arrow and box spotlight pointers, caption bubble
- step-by-step walkthroughs: a ‹ Step n/N · Next ▸ · ✕ bar; advance by voice in any language,
  by the Next button, or by clicking the highlighted target; a fresh screenshot for each step
- typed questions, a history window (search and copy), a skills folder, dictation in any
  language, a daily usage counter per provider
- Private mode with Ollama, any-language and mixed-language support
- a Velopack installer with "Check for updates"
- Rahbar's own overlay and step bar are left out of screenshots (Windows 10 2004+)

**Next:** Phase 3, agent mode (see `docs/PROMPT.md` §9), then Phase 4 SaaS. Phase 1 and 2 use
GDI capture; Windows.Graphics.Capture can replace it later if needed.
