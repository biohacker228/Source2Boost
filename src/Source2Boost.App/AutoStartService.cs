using System.Diagnostics;
using Source2Boost.Core;

namespace Source2Boost.App;

/// <summary>
/// Автозапуск приложения при входе в Windows через Планировщик задач с ВЫСШИМИ правами
/// (RunLevel=Highest) — так админ-приложение стартует без UAC-запроса на каждом логине
/// (обычный ключ HKCU\Run для requireAdministrator-exe спрашивал бы UAC каждый раз).
/// Запускается с аргументом --autostarted (окно стартует свёрнутым, работает сторож CS2).
/// </summary>
internal static class AutoStartService
{
    private const string TaskName = "Source2Boost_Autostart";

    public static bool IsEnabled()
    {
        var o = Run($"/query /tn {TaskName}");
        return o.exit == 0;
    }

    public static bool Enable()
    {
        var exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return false;
        // /rl highest — высшие права (без UAC), /sc onlogon — при входе, /f — перезаписать.
        var (exit, _) = Run($"/create /tn {TaskName} /tr \"\\\"{exe}\\\" --autostarted\" /sc onlogon /rl highest /f");
        Logger.Info($"autostart: enable -> exit {exit}");
        return exit == 0;
    }

    public static bool Disable()
    {
        var (exit, _) = Run($"/delete /tn {TaskName} /f");
        Logger.Info($"autostart: disable -> exit {exit}");
        return exit == 0;
    }

    private static (int exit, string outp) Run(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p is null) return (-1, "");
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, o);
        }
        catch (Exception ex) { Logger.Error("autostart: schtasks failed", ex); return (-1, ""); }
    }
}
