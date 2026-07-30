using System.IO;
using System.Threading;
using System.Windows;

namespace Source2Boost.App;

/// <summary>
/// Interaction logic for App.xaml. Управляет жизненным циклом: одна копия, значок в трее,
/// автозапуск в трей (--autostarted), закрытие окна = сворачивание в трей (сторож CS2 работает
/// в фоне), выход — только из меню трея.
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private System.Windows.Forms.NotifyIcon? _tray;
    private MainWindow? _mainWindow;
    private bool _exitRequested;
    private bool _trayHintShown;

    private const string ShowSignalName = "Source2Boost_Show";

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            // Раньше приложение молча закрывалось — пользователю нечего было прислать.
            // Теперь: пишем код в журнал, показываем его и ГАСИМ исключение (Handled=true).
            ShowCrashDialog(LogCrash(e.Exception));
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Ранний headless-режим --clean-standby: без UI, чистим standby-память и выходим.
        if (e.Args.Any(a => a.Equals("--clean-standby", StringComparison.OrdinalIgnoreCase)))
        {
            var force = e.Args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
            try { var r = StandbyMemoryCleaner.Run(force); Core.Logger.Info($"--clean-standby{(force ? " --force" : "")}: {r}"); }
            catch (Exception ex) { LogCrash(ex); }
            Shutdown();
            return;
        }

        // headless-тесты (--shot/--selftest/…) — без трея и без запрета второй копии.
        bool headless = e.Args.Any(a =>
            a.StartsWith("--shot=", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--selftest=", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--catalog=", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--apply-test=", StringComparison.OrdinalIgnoreCase) ||
            a.StartsWith("--nvapi-test=", StringComparison.OrdinalIgnoreCase));

        if (!headless)
        {
            _singleInstance = new Mutex(initiallyOwned: true, "Source2Boost_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // Уже запущены (возможно, в трее) — просим ту копию показать окно и тихо выходим.
                try { EventWaitHandle.OpenExisting(ShowSignalName).Set(); } catch { }
                Shutdown();
                return;
            }
            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            StartShowListener();
        }

        base.OnStartup(e);

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        if (!headless) InitTray();

        bool autostarted = e.Args.Any(a => a.Equals("--autostarted", StringComparison.OrdinalIgnoreCase));
        if (autostarted && !headless)
        {
            // Автозапуск с Windows → сразу в трей. Инициализируем окно скрытым (Loaded отработает,
            // сторож CS2 запустится), но в панели задач/ALT+TAB его нет — только значок в трее.
            _mainWindow.ShowActivated = false;
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowInTaskbar = false;
            _mainWindow.Show();
            _mainWindow.Hide();
        }
        else
        {
            _mainWindow.Show();
        }

        try { GameBoostService.ResumeLeftovers(); } catch { }
    }

    /// <summary>Значок в трее с меню «Открыть» / «Выход» и открытием по двойному клику.</summary>
    private void InitTray()
    {
        try
        {
            _tray = new System.Windows.Forms.NotifyIcon
            {
                Text = "Source2Boost",
                Visible = true,
                Icon = TrayIcon(),
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(SafeT("tray.open", "Открыть"), null, (_, _) => ShowMainWindow());
            menu.Items.Add(SafeT("tray.exit", "Выход"), null, (_, _) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (_, _) => ShowMainWindow();
        }
        catch (Exception ex) { LogCrash(ex); }
    }

    private static System.Drawing.Icon TrayIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (ico is not null) return ico;
            }
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>Показать/поднять главное окно (из трея или при повторном запуске ярлыка).</summary>
    public void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        _mainWindow.ShowInTaskbar = true;
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        _mainWindow.Topmost = true; _mainWindow.Topmost = false;   // вытащить поверх один раз
    }

    private void HideToTray()
    {
        if (_mainWindow is null) return;
        _mainWindow.Hide();
        _mainWindow.ShowInTaskbar = false;
    }

    /// <summary>Полный выход (из меню трея). Разрешает окну реально закрыться.</summary>
    public void ExitApp()
    {
        _exitRequested = true;
        Shutdown();
    }

    /// <summary>Вызывается из MainWindow.OnClosing. true — закрытие ОТМЕНИТЬ (свернули в трей).</summary>
    public bool HandleMainWindowClosing()
    {
        if (_exitRequested) return false;   // настоящий выход
        HideToTray();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            try { _tray?.ShowBalloonTip(4000, "Source2Boost", SafeT("tray.hint",
                "Свёрнут в трей и следит за запуском CS2. Правый клик по значку → «Выход», чтобы закрыть полностью."),
                System.Windows.Forms.ToolTipIcon.Info); } catch { }
        }
        return true;
    }

    private void StartShowListener()
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                try { _showSignal!.WaitOne(); } catch { break; }
                try { Dispatcher.BeginInvoke(new Action(ShowMainWindow)); } catch { break; }
            }
        }) { IsBackground = true, Name = "s2b-show-listener" };
        t.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { if (GameBoostService.IsBoosted) GameBoostService.Restore(); } catch { }
        try { if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
        base.OnExit(e);
    }

    /// <summary>Loc с фолбэком (на старте словарь уже готов, но подстрахуемся).</summary>
    private static string SafeT(string key, string fallback)
    {
        try { var v = Loc.T(key); return string.IsNullOrEmpty(v) || v == key ? fallback : v; }
        catch { return fallback; }
    }

    private static string LogCrash(Exception? ex)
    {
        var code = "S2B-" + DateTime.Now.ToString("MMddHHmmss");
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Boost");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] [{code}] {ex}\n\n");
        }
        catch { }
        try { if (ex is not null) Core.Logger.Error($"[{code}] unhandled", ex); } catch { }
        return code;
    }

    private static void ShowCrashDialog(string code)
    {
        try
        {
            string text;
            try { text = string.Format(Loc.T("error.crash"), code, Core.Logger.LogsDir); }
            catch { text = $"Source2Boost hit an error and will close.\n\nError code: {code}"; }
            MessageBox.Show(text, "Source2Boost", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { }
    }
}
