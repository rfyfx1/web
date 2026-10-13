using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Rahbar.App.Input;
using Rahbar.App.Overlay;
using Rahbar.App.Services;
using Rahbar.App.Windows;
using Rahbar.Core.Language;
using Rahbar.Core.Settings;
using Rahbar.Core.Storage;
using Rahbar.Providers;
using Serilog;

namespace Rahbar.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private TaskbarIcon? _tray;
    private HotkeyHook? _hook;
    private AssistantController? _assistant;
    private SettingsStore _store = null!;
    private CredentialStore _secrets = null!;
    private ProviderFactory _factory = null!;
    private MenuItem? _pauseItem, _privateItem, _usageItem, _skillMenu;
    private HistoryStore _history = null!;
    private UsageCounter _usage = null!;
    private SkillStore _skills = null!;
    private HistoryWindow? _historyWindow;
    private readonly List<MenuItem> _languageItems = new();

    internal AppSettings Settings { get; private set; } = new();
    internal static string DataFolder => SettingsStore.DefaultFolder;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, @"Local\Rahbar.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show("Rahbar is already running. Look for its icon near the clock.", "Rahbar");
            Shutdown();
            return;
        }

        Directory.CreateDirectory(DataFolder);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(DataFolder, "logs", "rahbar-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
        Log.Information("Rahbar starting");
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Fatal(args.ExceptionObject as Exception, "Fatal");
        TaskScheduler.UnobservedTaskException += (_, args) => { Log.Warning(args.Exception, "Unobserved task"); args.SetObserved(); };

        _store = new SettingsStore(Path.Combine(DataFolder, "settings.json"));
        Settings = _store.Load();
        _secrets = new CredentialStore();
        _factory = new ProviderFactory(new HttpClient { Timeout = TimeSpan.FromSeconds(45) }, _secrets);

        _history = new HistoryStore(Path.Combine(DataFolder, "history.json"));
        _usage = new UsageCounter(Path.Combine(DataFolder, "usage.json"));
        _skills = new SkillStore(Path.Combine(DataFolder, "skills"));
        try { _skills.EnsureExamples(); } catch (Exception ex) { Log.Warning(ex, "Couldn't create example skills"); }

        var overlays = new OverlayManager();
        _assistant = new AssistantController(() => Settings, _factory, overlays, _history, _usage, _skills, Path.Combine(DataFolder, "debug"));
        _assistant.StateChanged += OnStateChanged;
        _assistant.Problem += msg => { if (!string.IsNullOrEmpty(msg)) Notify("Rahbar", msg); };

        CreateTray();

        try
        {
            _hook = new HotkeyHook(Settings, Dispatcher) { Enabled = !Settings.Paused };
            _hook.Pressed += id =>
            {
                switch (id)
                {
                    case HotkeyId.Ask: _assistant.BeginListening(); break;
                    case HotkeyId.Typed: _assistant.AskTyped(); break;
                    case HotkeyId.Dictate: _assistant.BeginDictation(); break;
                }
            };
            _hook.Released += id =>
            {
                if (id == HotkeyId.Ask) _assistant.EndListening();
                else if (id == HotkeyId.Dictate) _assistant.EndDictation();
            };
            _hook.Escape += () => _assistant.Cancel();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Hotkey hook failed");
            MessageBox.Show("Rahbar couldn't set up its hotkey. Try restarting the PC.\n\n" + ex.Message, "Rahbar");
        }

        if (!Settings.FirstRunCompleted)
            ShowFirstRun();
        else
            Notify("Rahbar is ready", $"Hold {Settings.Hotkey} and ask your question. Release to get the answer.");
    }

    private void CreateTray()
    {
        var menu = new ContextMenu();
        _usageItem = new MenuItem { Header = _usage.Summary(), IsEnabled = false };
        menu.Items.Add(_usageItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Ask by voice (hold " + Settings.Hotkey + ")", (_, _) => Notify("How to ask", $"Hold {Settings.Hotkey}, speak, then let go.")));
        menu.Items.Add(Item("Type a question (" + Settings.TypedHotkey + ")", (_, _) => _assistant?.AskTyped()));
        menu.Items.Add(Item("History…", (_, _) => ShowHistory()));
        menu.Items.Add(new Separator());

        _privateItem = Item("Private mode (offline)", (_, _) => TogglePrivate());
        _privateItem.IsCheckable = true;
        _privateItem.IsChecked = Settings.PrivateMode;
        menu.Items.Add(_privateItem);

        var language = new MenuItem { Header = "Language" };
        foreach (var profile in LanguageProfile.All)
        {
            var p = profile;
            var mi = Item(p.DisplayName, (_, _) => SetLanguage(p.Preference));
            mi.IsCheckable = true;
            mi.Tag = p.Preference;
            _languageItems.Add(mi);
            language.Items.Add(mi);
        }
        menu.Items.Add(language);
        UpdateLanguageChecks();

        _skillMenu = new MenuItem { Header = "Skill" };
        _skillMenu.Items.Add(new MenuItem()); // placeholder so the submenu arrow shows; filled on open
        _skillMenu.SubmenuOpened += (_, _) => FillSkillMenu();
        menu.Items.Add(_skillMenu);

        menu.Items.Add(Item("Settings…", (_, _) => ShowSettings()));
        _pauseItem = Item(Settings.Paused ? "Resume Rahbar" : "Pause Rahbar", (_, _) => TogglePause());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Check for updates", async (_, _) => Notify("Rahbar", await UpdateService.CheckAndApplyAsync(Settings.UpdateUrl))));
        menu.Items.Add(Item("Open log folder", (_, _) => OpenFolder(Path.Combine(DataFolder, "logs"))));
        menu.Items.Add(Item("Quit", (_, _) => Shutdown()));
        menu.Opened += (_, _) => _usageItem.Header = _usage.Summary();

        _tray = new TaskbarIcon
        {
            ToolTipText = "Rahbar – hold " + Settings.Hotkey + " to ask",
            IconSource = Icon(Settings.Paused ? "rahbar-paused.ico" : "rahbar.ico"),
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.LeftOrRightClick,
        };
        _tray.ForceCreate(false);
    }

    private void FillSkillMenu()
    {
        if (_skillMenu is null) return;
        _skillMenu.Items.Clear();
        var none = Item("None", (_, _) => SetSkill(null));
        none.IsCheckable = true;
        none.IsChecked = string.IsNullOrEmpty(Settings.ActiveSkill);
        _skillMenu.Items.Add(none);
        foreach (var name in _skills.List())
        {
            var n = name;
            var mi = Item(n, (_, _) => SetSkill(n));
            mi.IsCheckable = true;
            mi.IsChecked = string.Equals(Settings.ActiveSkill, n, StringComparison.OrdinalIgnoreCase);
            _skillMenu.Items.Add(mi);
        }
        _skillMenu.Items.Add(new Separator());
        _skillMenu.Items.Add(Item("Open skills folder…", (_, _) => OpenFolder(_skills.Folder)));
    }

    private void SetSkill(string? name)
    {
        Settings.ActiveSkill = name;
        SaveSettings();
        if (name is not null) Notify("Rahbar", $"Skill on: {name}");
    }

    private void ShowHistory()
    {
        if (_historyWindow is { IsLoaded: true })
        {
            _historyWindow.Activate();
            return;
        }
        _historyWindow = new HistoryWindow(_history);
        _historyWindow.Show();
    }

    private static MenuItem Item(string header, RoutedEventHandler onClick)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += onClick;
        return mi;
    }

    private static BitmapImage Icon(string file) => new(new Uri($"pack://application:,,,/Assets/{file}"));

    private void OnStateChanged(AssistantState s)
    {
        if (_tray is null) return;
        _tray.IconSource = Icon(s switch
        {
            AssistantState.Listening or AssistantState.Dictating => "rahbar-listening.ico",
            AssistantState.Thinking => "rahbar-thinking.ico",
            _ when Settings.Paused => "rahbar-paused.ico",
            _ => "rahbar.ico",
        });
        _tray.ToolTipText = s switch
        {
            AssistantState.Listening => "Rahbar – listening…",
            AssistantState.Thinking => "Rahbar – thinking…",
            AssistantState.Speaking => "Rahbar – answering (Esc to stop)",
            AssistantState.WaitingForNext => "Rahbar – say \"next\" or click Next for the next step",
            AssistantState.Dictating => "Rahbar – dictating…",
            _ => "Rahbar – hold " + Settings.Hotkey + " to ask",
        };
    }

    internal void Notify(string title, string message)
    {
        try { _tray?.ShowNotification(title, message, NotificationIcon.Info); }
        catch (Exception ex) { Log.Warning(ex, "Notification failed"); }
    }

    private void SetLanguage(LanguagePreference p)
    {
        Settings.Language = p;
        SaveSettings();
        UpdateLanguageChecks();
    }

    private void UpdateLanguageChecks()
    {
        foreach (var mi in _languageItems) mi.IsChecked = (LanguagePreference)mi.Tag == Settings.Language;
    }

    private void TogglePrivate()
    {
        Settings.PrivateMode = !Settings.PrivateMode;
        _privateItem!.IsChecked = Settings.PrivateMode;
        SaveSettings();
        Notify("Rahbar", Settings.PrivateMode
            ? "Private mode ON: using the local Ollama model and offline speech. Nothing leaves this PC."
            : "Private mode OFF: using online AI again.");
    }

    private void TogglePause()
    {
        Settings.Paused = !Settings.Paused;
        if (_hook is not null) _hook.Enabled = !Settings.Paused;
        _pauseItem!.Header = Settings.Paused ? "Resume Rahbar" : "Pause Rahbar";
        if (Settings.Paused) _assistant?.Cancel();
        OnStateChanged(AssistantState.Idle);
        SaveSettings();
    }

    internal void SaveSettings()
    {
        try { _store.Save(Settings); }
        catch (Exception ex) { Log.Error(ex, "Saving settings failed"); }
    }

    internal void ApplySettings()
    {
        SaveSettings();
        if (_hook is not null)
        {
            _hook.Update(Settings);
            _hook.Enabled = !Settings.Paused;
        }
        _privateItem!.IsChecked = Settings.PrivateMode;
        UpdateLanguageChecks();
        _assistant?.ReloadSpeech();
        OnStateChanged(AssistantState.Idle);
    }

    private SettingsWindow? _settingsWindow;

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(Settings, _secrets, _factory);
        _settingsWindow.Saved += ApplySettings;
        // Pause the hotkeys while Settings is open, so the hotkey boxes can record them.
        if (_hook is not null) _hook.Enabled = false;
        _settingsWindow.Closed += (_, _) => { if (_hook is not null) _hook.Enabled = !Settings.Paused; };
        _settingsWindow.Show();
    }

    private void ShowFirstRun()
    {
        if (_hook is not null) _hook.Enabled = false; // don't trigger while typing keys
        var wizard = new FirstRunWindow(Settings, _secrets, _factory);
        wizard.Closed += (_, _) =>
        {
            if (wizard.Completed)
            {
                Settings.FirstRunCompleted = true;
                ApplySettings();
                Notify("Rahbar is ready", $"Hold {Settings.Hotkey}, ask your question, then let go.");
            }
            else if (_hook is not null)
            {
                _hook.Enabled = !Settings.Paused;
            }
        };
        wizard.Show();
    }

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hook?.Dispose();
        _assistant?.Dispose();
        _tray?.Dispose();
        Log.Information("Rahbar stopped");
        Log.CloseAndFlush();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
