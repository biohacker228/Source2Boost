using System.Runtime.InteropServices;
using Source2Boost.Core;

namespace Source2Boost.App;

/// <summary>
/// Headless-очистка standby-списка памяти (ISLC-подобно) для режима <c>--clean-standby</c>.
/// Читает свободную физическую память через GlobalMemoryStatusEx и чистит standby-список
/// через NtSetSystemInformation(SystemMemoryListInformation, MemoryPurgeStandbyList) —
/// но ТОЛЬКО когда свободной памяти мало (иначе смысла нет, как в ISLC).
/// Требует привилегию SeProfileSingleProcessPrivilege (включается перед вызовом).
/// Никогда не показывает окно; пишет итог в Logger.
/// </summary>
internal static class StandbyMemoryCleaner
{
    // --- NtSetSystemInformation: класс и команда ---
    private const int SystemMemoryListInformation = 0x50; // 80
    private const int MemoryPurgeStandbyList = 4;

    // Порог: чистим, если доступной памяти < 15% от общей ИЛИ < 2 ГБ.
    private const double LowFreeFraction = 0.15;
    private const ulong LowFreeBytes = 2UL * 1024 * 1024 * 1024;

    /// <summary>
    /// Очистка standby-списка. При <paramref name="force"/>=true чистит безусловно
    /// (для ежедневной задачи, даже если RAM много); иначе — только при нехватке памяти (ISLC-подобно).
    /// Возвращает короткий итог для лога.
    /// </summary>
    public static string Run(bool force = false)
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref mem))
            {
                var err = Marshal.GetLastWin32Error();
                Logger.Warn($"standby-clean: GlobalMemoryStatusEx failed (err {err})");
                return "memstatus-failed";
            }

            var total = mem.ullTotalPhys;
            var avail = mem.ullAvailPhys;
            var lowByFraction = total > 0 && avail < (ulong)(total * LowFreeFraction);
            var lowByAbsolute = avail < LowFreeBytes;

            var availGb = avail / 1024.0 / 1024.0 / 1024.0;
            var totalGb = total / 1024.0 / 1024.0 / 1024.0;

            if (!force && !lowByFraction && !lowByAbsolute)
            {
                Logger.Info($"standby-clean: skip (avail {availGb:F2} GB / {totalGb:F2} GB, load {mem.dwMemoryLoad}%)");
                return "skip-plenty-free";
            }

            if (!EnableProfilePrivilege())
                Logger.Warn("standby-clean: could not enable SeProfileSingleProcessPrivilege (need admin)");

            int command = MemoryPurgeStandbyList;
            var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
            if (status == 0)
            {
                Logger.Info($"standby-clean: purged standby list (was avail {availGb:F2} GB / {totalGb:F2} GB, load {mem.dwMemoryLoad}%)");
                return "purged";
            }

            Logger.Warn($"standby-clean: NtSetSystemInformation returned NTSTATUS 0x{status:X8}");
            return "purge-failed";
        }
        catch (Exception ex)
        {
            Logger.Error("standby-clean: exception", ex);
            return "exception";
        }
    }

    /// <summary>Включить SeProfileSingleProcessPrivilege в токене текущего процесса.</summary>
    private static bool EnableProfilePrivilege()
    {
        const string SeProfileSingleProcessPrivilege = "SeProfileSingleProcessPrivilege";
        const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        const int TOKEN_QUERY = 0x0008;
        const int SE_PRIVILEGE_ENABLED = 0x00000002;

        var hProcess = GetCurrentProcess();
        if (!OpenProcessToken(hProcess, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var hToken))
            return false;
        try
        {
            if (!LookupPrivilegeValue(null, SeProfileSingleProcessPrivilege, out var luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };
            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                return false;
            // AdjustTokenPrivileges может вернуть true, но ERROR_NOT_ALL_ASSIGNED — проверяем.
            return Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(hToken); }
    }

    // ---------- P/Invoke ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int SystemInformationClass, ref int SystemInformation, int SystemInformationLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
