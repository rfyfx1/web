# Build prompt: "Rahbar", a screen-aware voice AI buddy for Windows

> Paste this whole file into Claude Code, or save it in the project folder as `PROMPT.md` and tell Claude Code: "Read PROMPT.md and build Phase 1."
> "Rahbar" (Urdu for "guide") is a working name and can be changed anywhere.

---

## 1. What we are building

A Windows desktop app that sits quietly in the system tray. When the user presses a hotkey:

1. It takes a screenshot of the screen the mouse is on.
2. It listens to the user's spoken question (push-to-talk: hold the hotkey, speak, release).
3. It sends the screenshot and the question to an AI vision model.
4. It speaks the answer back in a natural voice.
5. It draws on top of the screen, with a pulsing circle, an arrow or a highlight box, on the exact button or area the answer refers to. For multi-step help, it shows step 1, waits for the user to say "next" or click, then shows step 2.

It is inspired by heyclicky (Mac only). Our version is **Windows-first**, and users can talk in **any language, including mixed speech**: English, Urdu, Roman Urdu, Hindi, Hinglish and others. Rahbar replies in the same language, script and mix the user used, and is first used **inside our own organisation for free**. Later it becomes a paid SaaS.

Main uses for our team: learning tools (Seller Central, Temu Seller Centre, TikTok Shop Seller Center, Canva, CapCut, Premiere, Excel, Figma), getting unstuck in any app, and having the screen explained.

---

## 2. Hard rules

- **Never record the screen continuously.** Take a screenshot only when the hotkey is pressed. Show a visible indicator (glowing edge or tray icon change) while capturing or listening.
- **API keys are never hard-coded** and never written to logs. Store them with Windows Credential Manager or DPAPI-encrypted user settings.
- **Every AI provider sits behind one interface** so it can be swapped in settings without code changes.
- **Free mode must work end to end at zero cost** (see Section 5).
- **Agent mode (Phase 3) must ask for confirmation** before anything that sends, buys, deletes, posts or submits.
- Keep the UI simple and friendly. Our team are not developers.

---

## 3. Tech stack

- **Language/UI:** C# on **.NET 8**, **WPF** for the windows and overlay.
- **Tray icon:** `H.NotifyIcon.Wpf` (or similar maintained package).
- **Global hotkey:** Win32 `RegisterHotKey` for tap actions, plus a low-level keyboard hook (`SetWindowsHookEx` WH_KEYBOARD_LL) for hold-to-talk. Default: hold **Ctrl + Space**. User can change it.
- **Screen capture:** `Windows.Graphics.Capture` API (fast, DPI-correct). Fallback: GDI `BitBlt`. Capture **only the monitor under the cursor**. Downscale to a maximum of 1568px on the long edge before sending, and keep the scale factor to map coordinates back.
- **Audio capture:** `NAudio` (16 kHz mono WAV, which is what speech models want).
- **Overlay:** a borderless, transparent, topmost, **click-through** WPF window per monitor (`WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`). Per-Monitor-V2 DPI aware via the app manifest.
- **Text-to-speech:** Windows built-in voices via `Windows.Media.SpeechSynthesis` (free, offline). Optional premium voices later behind the same interface.
- **Settings storage:** JSON in `%AppData%\Rahbar\settings.json`; secrets via Credential Manager.
- **Logging:** `Serilog` to `%AppData%\Rahbar\logs` (no keys, no screenshots unless debug mode is on).
- **Installer:** **Velopack** (installer plus auto-update). Unsigned for internal use; code signing added in Phase 4.
- **Project layout:**
  ```
  Rahbar.sln
  src/Rahbar.App          (WPF app: tray, overlay, windows)
  src/Rahbar.Core         (pipeline, interfaces, prompt building, coordinate mapping)
  src/Rahbar.Providers    (Gemini, Claude, Ollama, Groq, Windows speech)
  tests/Rahbar.Core.Tests (xUnit)
  ```

---

## 4. Provider interfaces (swappable AI)

```csharp
interface IVisionBrain     { Task<BrainReply> AskAsync(BrainRequest req, CancellationToken ct); }
interface ISpeechToText    { Task<string> TranscribeAsync(byte[] wav, string? languageHint, CancellationToken ct); }
interface ITextToSpeech    { Task SpeakAsync(string text, string language, CancellationToken ct); void Stop(); }
```

Implement these providers:

| Role | Provider | Cost | Notes |
|---|---|---|---|
| Vision brain | **Google Gemini (Flash)** | Free tier / very cheap paid | Default for free mode |
| Vision brain | **Anthropic Claude** | Paid API | Best pointing accuracy; for paid/SaaS mode |
| Vision brain | **Ollama (local)** e.g. Qwen2.5-VL or Llama 3.2 Vision | Free, private | Needs a good GPU; nothing leaves the PC |
| Speech-to-text | **Groq Whisper** (`whisper-large-v3-turbo`) | Free tier | Default; supports Urdu |
| Speech-to-text | **Windows built-in recognition** | Free, offline | Fallback when no internet/key |
| Text-to-speech | **Windows voices** | Free, offline | Default |

Settings screen: choose a provider per role, paste the key, press **Test** to send a tiny request and show OK/error.

Handle rate limits (HTTP 429) with retry and backoff, and show a friendly message like "Free limit reached, try again in a minute."

---

## 5. Free mode (organisation use at Rs 0)

- Brain = Gemini Flash free tier. Voice in = Groq Whisper free tier. Voice out = Windows voices.
- Each team member puts in **their own** free keys during first-run setup, so limits are per person.
- First-run wizard: (1) pick language, (2) paste Gemini key with link to get one, (3) paste Groq key with link, (4) test mic, (5) pick hotkey, (6) done.
- Show a one-line privacy note in setup: free-tier AI providers may use requests to improve their models, so don't use it on screens with private data, **or** switch to Ollama (local) for private work.
- Add a **"Private mode" toggle** in the tray menu that forces the Ollama brain and Windows speech recognition (fully offline).

---

## 6. The core pipeline

1. **Hotkey down:** capture the screenshot immediately (before any windows change), start mic recording, show a "listening" glow at the screen edge.
2. **Hotkey up:** stop recording. If it's under 0.4s with no speech, treat it as "explain what I'm looking at."
3. **Transcribe** the audio, with an auto language hint from settings.
4. **Gather context:** active window title and process name (via `GetForegroundWindow`), cursor position, monitor bounds and DPI scale, last 3 Q&A turns of this session (short memory).
5. **Ask the brain** with the system prompt below, the screenshot and the question.
6. **Parse the JSON reply.** If parsing fails, retry once with "Return only valid JSON."
7. **Speak** the answer and **draw** the pointer(s) at the same time.
8. **Esc** at any time stops speech and clears the overlay.

### Brain system prompt (put in `Rahbar.Core/Prompts/brain_system.txt`)

```
You are Rahbar, a friendly helper that can see the user's screen.
The user is looking at the screenshot provided. Active app: {window_title} ({process_name}).
Answer their question about what is on screen. Be short and spoken-friendly:
max 2-3 sentences per step, no markdown, no lists read aloud.
Reply in the same language the user spoke (English, Urdu, or Roman Urdu).
If they spoke Urdu, reply in simple Urdu.

When the answer involves clicking or looking at something, give its location.
Coordinates are in pixels of the screenshot you received (width {w}, height {h}),
origin top-left. Point to the CENTER of the target. Only point at things you can
actually see. If the target is not visible, say where to go first (e.g. scroll, open a menu)
and point at that instead.

Return ONLY JSON in this shape:
{
  "steps": [
    {
      "say": "text to speak for this step",
      "point": {"x": 0, "y": 0, "type": "circle|arrow|box", "w": 0, "h": 0, "label": "short label"} | null
    }
  ],
  "language": "en|ur",
  "needs_more_steps": false
}
Use one step for simple answers. Use several steps for walkthroughs (max 6).
Never invent buttons. If you are unsure, say so.
```

### Coordinate mapping (must be unit tested)

`screen_x = monitor.Left + (x / downscale) / dpiScale` adjusted correctly for Per-Monitor-V2. Write tests for 100%, 125%, 150% and 200% scaling and for a second monitor placed left of and above the primary (negative coordinates).

---

## 7. The overlay (the part that makes it feel magic)

- **Circle:** soft pulsing ring (accent colour, about 48px, 1.2s pulse) at the target, with an optional small label bubble.
- **Arrow:** curved arrow from near the cursor to the target.
- **Box:** rounded highlight rectangle around an area with a dimmed outside (spotlight effect, about 40% dim).
- **Caption bubble:** small subtitle near the bottom of the screen showing what is being said (helps in noisy offices and for Urdu readers).
- **Step controls:** for multi-step answers, a tiny floating pill showing "Step 2/4 · Next ▸ · ✕". Advance when the user says "next" or "agla" (hold hotkey), clicks Next, or clicks inside the highlighted target (detect via a low-level mouse hook; the overlay itself stays click-through).
- Auto-clear 8s after the last step finishes speaking, or on Esc.
- Smooth fade in/out, 60fps, GPU-friendly (WPF animations, no per-frame allocations).

---

## 8. Other features

- **Tray menu:** Ask (same as hotkey), Private mode toggle, Language (Auto/English/Urdu), Settings, History, Pause Rahbar, Quit.
- **Typed question:** `Ctrl + Shift + Space` opens a small text box near the cursor (for quiet offices).
- **History window:** last 50 questions and answers (text only, no screenshots by default), with search and a copy button.
- **Dictation (Phase 2):** a hotkey that types whatever you say into the focused text field (via `SendInput`), in English or Urdu.
- **Custom "skills" (Phase 2):** a folder of `.md` files with extra instructions (e.g. "Seller Central guide", "our brand voice"). The user picks one in the tray menu and it is added to the system prompt.
- **Usage counter:** shows today's requests per provider so free limits are visible.

---

## 9. Agent mode (Phase 3)

Trigger: start the question with "Rahbar agent ..." / "agent ...".

- Runs a loop: screenshot → model decides one action (click x,y / type text / press keys / scroll / wait / done) → execute with `SendInput` → screenshot again. Max 25 steps, then stop and report.
- Use a model good at computer control (Claude with its computer-use tool is preferred for this mode; keep it behind the same provider interface).
- Show a red border while the agent is in control. **Esc** or moving the mouse hard aborts immediately.
- **Confirmation dialog** before send, post, buy, pay, delete, submit, publish or anything with money. Never type passwords; ask the user to do it.
- Log every action taken to a visible list the user can review.

---

## 10. SaaS version (Phase 4, later)

- **Backend:** small API (ASP.NET Core minimal API or Supabase Edge Functions) that holds **our** AI keys, so customers don't need their own. The app calls our backend, and the backend calls the AI. Never ship our keys inside the app.
- **Accounts:** Supabase Auth (email and Google sign-in). App login via a browser redirect to a localhost callback.
- **Plans:** Free (e.g. 20 questions/day), Pro, Team (shared skills, central billing). Enforce limits on the server.
- **Payments:** Stripe or Paddle (Paddle handles global tax; check which supports a Pakistan-based business at build time), plus a local option (e.g. JazzCash/Easypaisa via a gateway) for Pakistani customers.
- **Usage metering:** count tokens/requests per user per day on the server. Admin page showing cost per user versus revenue.
- **Code signing certificate** plus Velopack auto-updates, and a simple landing page with a download button.
- **Privacy:** no screenshots stored on the server; requests are processed and discarded. Write a clear privacy page.

---

## 11. Build phases and what "done" means

**Phase 1: Core helper (build this first)**
- Tray app, hotkey hold-to-talk, screenshot of the current monitor, Groq transcription, Gemini answer, Windows voice reply, circle pointer, Esc to cancel, settings with key test, first-run wizard.
- ✅ Done when: on a 1080p and a 4K/150% screen, asking "where is the save button?" in Notepad, Excel and Chrome puts the circle on the right button at least 8 out of 10 times, and the voice answer starts within about 3 seconds.

**Phase 2: Polish for the team**
- Arrow/box/spotlight, multi-step walkthroughs, caption bubble, Urdu end-to-end, typed questions, history, private mode with Ollama, skills folder, dictation, Velopack installer.
- ✅ Done when: a non-technical teammate can install it, finish setup in under 3 minutes, and complete a 4-step walkthrough in Urdu.

**Phase 3: Agent mode** (Section 9).

**Phase 4: SaaS** (Section 10).

---

## 12. How to work

- Start by scaffolding the solution and a tiny "hello tray icon" build. Confirm it runs, then build Phase 1 step by step.
- After each step: build, run tests, and tell me in plain simple language what works now and how to try it.
- Write unit tests for coordinate mapping, JSON parsing and provider fallbacks.
- Keep a `README.md` with: how to install, how to get free Gemini and Groq keys, the hotkeys, and troubleshooting.
- Check each provider's current API docs before writing the client code; model names and limits change often.
- If something needs my decision, ask one short question with options.
