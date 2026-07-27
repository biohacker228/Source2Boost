using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Source2Boost.Core;

namespace Source2Boost.App;

/// <summary>
/// «Игровой фокус» (Boost Mode): временно УСЫПЛЯЕТ известные фоновые приложения (браузеры,
/// мессенджеры, медиа), чтобы освободить ядра CPU и RAM под CS2, и чистит standby-память.
/// По выходу из режима — будит их обратно. На CPU-bound системе это заметно поднимает 1% low
/// и убирает статтер от фоновой активности (вкладки браузера, облачные синхронизации, апдейтеры).
/// Discord/голос/музыку НЕ трогаем — ими пользуются прямо в игре (см. список Suspendable ниже).
///
/// БЕЗОПАСНОСТЬ: усыпляем ТОЛЬКО процессы из белого списка (никогда систему/Steam/CS2/себя).
/// Приостановленные PID пишутся в файл, чтобы гарантированно разбудить их даже после
/// перезапуска/краша приложения (ResumeLeftovers при старте + авто-Restore на выходе).
/// </summary>
internal static class GameBoostService
{
    private const int PROCESS_SUSPEND_RESUME = 0x0800;

    /// <summary>Белый список фоновых приложений, которые безопасно усыплять на время игры
    /// (имена процессов без .exe, регистр не важен).
    /// НАМЕРЕННО НЕ включаем: голос/чат (Discord, Telegram, TeamSpeak и т.п. — ими активно
    /// пользуются В игре), стриминг (OBS), музыку/медиа-плееры, Steam/steamwebhelper (оверлей/друзья).
    /// Оставляем только реальных «фоновых пожирателей», которые не нужны во время матча:
    /// браузеры (тяжёлые вкладки), облачные синхронизации, лончеры-апдейтеры.</summary>
    private static readonly HashSet<string> Suspendable = new(StringComparer.OrdinalIgnoreCase)
    {
        // Браузеры (главный источник фоновой нагрузки от вкладок)
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore",
        "browser", // Yandex Browser
        // Облачные синхронизации
        "dropbox", "onedrive", "yandexdisk", "googledrivefs", "megasync",
        // Почта / лончеры-апдейтеры (не игровой процесс)
        "thunderbird", "epicgameslauncher", "battle.net", "riotclientux",
    };

    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Source2Boost", "boost-suspended.txt");

    /// <summary>Режим сейчас активен (есть усыплённые процессы)?</summary>
    public static bool IsBoosted => File.Exists(StateFile);

    /// <summary>Усыпить фоновые приложения + почистить standby. Возвращает (сколько усыплено, имена).</summary>
    public static (int count, string names) Boost()
    {
        var suspended = new List<int>();
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int self = Environment.ProcessId;
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self) continue;
                if (!Suspendable.Contains(p.ProcessName)) continue;
                if (SuspendProcess(p.Id)) { suspended.Add(p.Id); names.Add(p.ProcessName); }
            }
            catch { /* процесс мог исчезнуть/нет доступа — пропускаем */ }
            finally { p.Dispose(); }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllLines(StateFile, suspended.Select(i => i.ToString()));
        }
        catch (Exception ex) { Logger.Error("boost: cannot write state", ex); }

        // Разово освобождаем standby-память (безусловно) — после усыпления фон отпустил её.
        try { StandbyMemoryCleaner.Run(force: true); } catch { }

        Logger.Info($"boost: suspended {suspended.Count} procs [{string.Join(",", names)}]");
        return (suspended.Count, string.Join(", ", names));
    }

    /// <summary>Разбудить всё, что усыпляли (по файлу состояния), и снять режим.</summary>
    public static int Restore()
    {
        int resumed = 0;
        try
        {
            if (File.Exists(StateFile))
            {
                foreach (var line in File.ReadAllLines(StateFile))
                    if (int.TryParse(line.Trim(), out var pid) && ResumeProcess(pid)) resumed++;
                File.Delete(StateFile);
            }
        }
        catch (Exception ex) { Logger.Error("boost: restore failed", ex); }
        Logger.Info($"boost: resumed {resumed} procs");
        return resumed;
    }

    /// <summary>Подстраховка при старте приложения: если прошлый сеанс упал/закрылся в режиме
    /// Boost — будим оставшиеся усыплённые процессы, чтобы не оставить браузер «замороженным».</summary>
    public static void ResumeLeftovers()
    {
        if (IsBoosted)
        {
            var n = Restore();
            if (n > 0) Logger.Info($"boost: resumed {n} leftover procs on startup");
        }
    }

    private static bool SuspendProcess(int pid)
    {
        var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return NtSuspendProcess(h) == 0; }
        finally { CloseHandle(h); }
    }

    private static bool ResumeProcess(int pid)
    {
        var h = OpenProcess(PROCESS_SUSPEND_RESUME, false, pid);
        if (h == IntPtr.Zero) return false;
        try { return NtResumeProcess(h) == 0; }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, int dwProcessId);

    [DllImport("ntdll.dll")] private static extern int NtSuspendProcess(IntPtr processHandle);
    [DllImport("ntdll.dll")] private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
