using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Source2Boost.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstance;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            // Раньше приложение молча закрывалось — пользователю нечего было прислать.
            // Теперь: пишем код в журнал, показываем его и ГАСИМ исключение (Handled=true),
            // чтобы приложение продолжило работу. Ошибка уже произошла — закрытие её не отменит,
            // а живое окно даёт откатить твики и скопировать код.
            ShowCrashDialog(LogCrash(e.Exception));
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception);
    }

    /// <summary>
    /// Ранний перехват headless-режима <c>--clean-standby</c>: выполняется ДО создания
    /// MainWindow (StartupUri), поэтому окно НЕ показывается. Условно чистит standby-список
    /// памяти и немедленно завершает процесс. Обычный запуск идёт через base.OnStartup.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(a => a.Equals("--clean-standby", StringComparison.OrdinalIgnoreCase)))
        {
            var force = e.Args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));
            try { var r = StandbyMemoryCleaner.Run(force); Core.Logger.Info($"--clean-standby{(force ? " --force" : "")}: {r}"); }
            catch (Exception ex) { LogCrash(ex); }
            // Никакого UI: завершаемся быстро, MainWindow (StartupUri) не создаётся.
            Shutdown();
            return;
        }

        // Запрет второй копии (кроме headless-режимов --shot/--selftest, которые запускаются отдельно).
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
                MessageBox.Show("Source2Boost уже запущен.", "Source2Boost",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);

        // Подстраховка: если прошлый сеанс упал в режиме Boost — будим оставшиеся усыплённые процессы.
        try { GameBoostService.ResumeLeftovers(); } catch { }
    }

    /// <summary>При выходе ВСЕГДА будим усыплённые Boost-режимом процессы, чтобы не оставить
    /// браузер/мессенджер «замороженными».</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try { if (GameBoostService.IsBoosted) GameBoostService.Restore(); } catch { }
        base.OnExit(e);
    }

    /// <summary>Пишет краш в crash.log и журнал, возвращает короткий код для пользователя.</summary>
    private static string LogCrash(Exception? ex)
    {
        var code = "S2B-" + DateTime.Now.ToString("MMddHHmmss");
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Boost");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:O}] [{code}] {ex}\n\n");
        }
        catch { /* ignore */ }
        try { if (ex is not null) Core.Logger.Error($"[{code}] unhandled", ex); } catch { }
        return code;
    }

    /// <summary>Показать код краша. В аварийном пути намеренно системный MessageBox (тематический
    /// диалог требует живого UI-стека, который в этот момент может быть уже сломан).</summary>
    private static void ShowCrashDialog(string code)
    {
        try
        {
            string text;
            try { text = string.Format(Loc.T("error.crash"), code, Core.Logger.LogsDir); }
            catch { text = $"Source2Boost hit an error and will close.\n\nError code: {code}"; }
            MessageBox.Show(text, "Source2Boost", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch { /* ничего не поделать */ }
    }
}
