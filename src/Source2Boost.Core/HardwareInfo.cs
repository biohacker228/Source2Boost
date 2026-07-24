using System.Management;
using System.Runtime.InteropServices;

namespace Source2Boost.Core;

/// <summary>Снимок железа/ОС для диагностики и выбора применимых твиков.</summary>
public sealed class HardwareInfo
{
    public string CpuName { get; init; } = "";
    public int CpuCores { get; init; }
    public int CpuThreads { get; init; }
    public string GpuVendor { get; init; } = ""; // NVIDIA / AMD / Intel
    public string GpuName { get; init; } = "";
    public int RamMb { get; init; }
    /// <summary>Фактическая рабочая частота памяти (ConfiguredClockSpeed), МГц.</summary>
    public int RamSpeedMhz { get; init; }
    /// <summary>Паспортная (SPD/XMP) частота памяти (Speed), МГц. 0 если неизвестна.</summary>
    public int RamRatedMhz { get; init; }
    public bool RamMixedKit { get; init; }
    public int RamModules { get; init; }
    public int MonitorHz { get; init; }
    public int MonitorCount { get; init; }
    public string WindowsCaption { get; init; } = "";
    public int WindowsBuild { get; init; }
    /// <summary>SSD ли диск с игрой: true=SSD, false=HDD, null=не удалось определить.</summary>
    public bool? Cs2OnSsd { get; init; }
    /// <summary>Есть ли дискретная видеокарта (NVIDIA/AMD). false = только встройка.</summary>
    public bool HasDiscreteGpu { get; init; }

    public bool IsNvidia => GpuVendor.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase);
    public bool IsAmd => GpuVendor.Equals("AMD", StringComparison.OrdinalIgnoreCase);
    /// <summary>Одноканальная память (одна планка) — крупное узкое место на слабом ПК.</summary>
    public bool RamSingleChannel => RamModules == 1;
    public int RamGb => (int)Math.Round(RamMb / 1024.0);
    /// <summary>Память работает ЗАМЕТНО ниже паспортной частоты — верный признак, что XMP/DOCP
    /// не включён в BIOS. Порог 100 МГц гасит мелкий шум округления.</summary>
    public bool RamRunningBelowRated => RamRatedMhz > 0 && RamSpeedMhz > 0 && RamSpeedMhz + 100 < RamRatedMhz;

    /// <summary>
    /// Короткое имя CPU для карточек UI. WMI отдаёт «Intel(R) Core(TM) i7-6700 CPU @ 3.40GHz» —
    /// в узкую карточку это не влезает и обрезается многоточием. Чистим маркетинговый шум,
    /// оставляя модель: «Intel Core i7-6700», «AMD Ryzen 5 3600».
    /// </summary>
    public string CpuShort => Compact(System.Text.RegularExpressions.Regex.Replace(
        CpuName,
        @"\((?:R|TM)\)|\bCPU\b|@\s*[\d.]+\s*[GM]Hz|\b\d+-Core\b|\bProcessor\b|\bwith\s+Radeon\s+Graphics\b",
        " ",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    /// <summary>Короткое имя GPU: «NVIDIA GeForce GTX 1650 SUPER» → «GeForce GTX 1650 SUPER»
    /// (вендор и так понятен по названию серии и показан отдельно).</summary>
    public string GpuShort
    {
        get
        {
            var g = System.Text.RegularExpressions.Regex.Replace(GpuName, @"\((?:R|TM)\)", " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            g = System.Text.RegularExpressions.Regex.Replace(g, @"^\s*(NVIDIA|AMD|Intel)\s+", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return Compact(g);
        }
    }

    /// <summary>Схлопнуть повторные пробелы и обрезать края.</summary>
    private static string Compact(string s) =>
        System.Text.RegularExpressions.Regex.Replace(s, @"\s{2,}", " ").Trim();

    /// <summary>Собрать снимок с текущей машины через WMI.</summary>
    public static HardwareInfo Detect()
    {
        string cpuName = ""; int cores = 0, threads = 0;
        foreach (var o in Query("SELECT Name,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor"))
        {
            cpuName = (o["Name"] as string ?? "").Trim();
            cores = ToInt(o["NumberOfCores"]);
            threads = ToInt(o["NumberOfLogicalProcessors"]);
            break;
        }

        int ram = 0, speed = 0, rated = 0; int modules = 0;
        var speeds = new HashSet<int>();
        var caps = new HashSet<long>();
        var mans = new HashSet<string>();
        foreach (var o in Query("SELECT Capacity,ConfiguredClockSpeed,Speed,Manufacturer FROM Win32_PhysicalMemory"))
        {
            long cap = ToLong(o["Capacity"]);
            ram += (int)(cap / (1024 * 1024));
            caps.Add(cap);
            mans.Add((o["Manufacturer"] as string ?? "").Trim());
            int cfg = ToInt(o["ConfiguredClockSpeed"]);          // фактическая рабочая частота
            int spd = ToInt(o["Speed"]);                          // паспортная (SPD/XMP) частота
            if (cfg == 0) cfg = spd;
            if (cfg > 0) { speed = Math.Max(speed, cfg); speeds.Add(cfg); }
            if (spd > 0) rated = Math.Max(rated, spd);
            modules++;
        }
        // Разнокалиберный набор: разные объёмы / скорости / производители планок
        bool mixedKit = modules > 1 && (speeds.Count > 1 || caps.Count > 1 || mans.Count > 1);

        string gpuVendor = "", gpuName = ""; int hz = 0; bool hasDiscrete = false;
        foreach (var o in Query("SELECT Name,CurrentRefreshRate,VideoProcessor FROM Win32_VideoController"))
        {
            var name = (o["Name"] as string ?? "");
            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) { gpuVendor = "NVIDIA"; gpuName = name; hasDiscrete = true; }
            else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) { hasDiscrete = true; if (gpuVendor is "" or "Intel" or "Unknown") { gpuVendor = "AMD"; gpuName = name; } }
            else if (gpuVendor == "") { gpuVendor = name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Unknown"; gpuName = name; }
            hz = Math.Max(hz, ToInt(o["CurrentRefreshRate"]));
        }

        string osCaption = ""; int build = 0;
        foreach (var o in Query("SELECT Caption,BuildNumber FROM Win32_OperatingSystem"))
        {
            osCaption = (o["Caption"] as string ?? "").Trim();
            int.TryParse(o["BuildNumber"] as string, out build);
            break;
        }

        return new HardwareInfo
        {
            CpuName = cpuName, CpuCores = cores, CpuThreads = threads,
            GpuVendor = gpuVendor, GpuName = gpuName, HasDiscreteGpu = hasDiscrete,
            RamMb = ram, RamSpeedMhz = speed, RamRatedMhz = rated, RamMixedKit = mixedKit, RamModules = modules,
            MonitorHz = PrimaryRefreshHz() is var r && r > 0 ? r : hz,
            MonitorCount = Math.Max(1, GetSystemMetrics(SM_CMONITORS)),
            Cs2OnSsd = DetectCs2Ssd(),
            WindowsCaption = osCaption, WindowsBuild = build
        };
    }

    private const int SM_CMONITORS = 80;
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    /// <summary>SSD ли диск, где лежит cs2.exe. null если определить не удалось.</summary>
    private static bool? DetectCs2Ssd()
    {
        try
        {
            var exe = Cs2Paths.Cs2ExePath();
            if (string.IsNullOrEmpty(exe) || exe.Length < 2 || exe[1] != ':') return null;
            var letter = char.ToUpperInvariant(exe[0]);

            // MSFT_Partition(DriveLetter) -> DiskNumber -> MSFT_PhysicalDisk.MediaType (3=HDD,4=SSD)
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            int diskNum = -1;
            using (var partSearch = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT DiskNumber,DriveLetter FROM MSFT_Partition WHERE DriveLetter='{letter}'")))
                foreach (ManagementBaseObject p in partSearch.Get()) { diskNum = Convert.ToInt32(p["DiskNumber"]); break; }
            if (diskNum < 0) return null;

            using var pdSearch = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT MediaType,DeviceId FROM MSFT_PhysicalDisk WHERE DeviceId='{diskNum}'"));
            foreach (ManagementBaseObject d in pdSearch.Get())
            {
                var mt = Convert.ToInt32(d["MediaType"]); // 3=HDD, 4=SSD, 5=SCM
                if (mt == 4 || mt == 5) return true;
                if (mt == 3) return false;
            }
            return null;
        }
        catch { return null; }
    }

    private static IEnumerable<ManagementBaseObject> Query(string wql)
    {
        using var s = new ManagementObjectSearcher(wql);
        foreach (ManagementBaseObject o in s.Get()) yield return o;
    }
    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    private static long ToLong(object? v) => v is null ? 0 : Convert.ToInt64(v);

    // --- Частота обновления через display-API (WMI CurrentRefreshRate врёт на 180 Гц).
    // ВАЖНО при нескольких мониторах: берём МАКСИМАЛЬНУЮ частоту среди всех подключённых
    // экранов (игровой монитор — самый «быстрый»), а не primary (может быть 60 Гц). ---
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    private static int PrimaryRefreshHz()
    {
        int max = 0;
        try
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++)
            {
                if ((dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    if (EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                        max = Math.Max(max, dm.dmDisplayFrequency);
                }
                dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            }
            if (max == 0) // фолбэк на primary
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) max = dm.dmDisplayFrequency;
            }
        }
        catch { }
        return max;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2;
        public int dmPanningWidth, dmPanningHeight;
    }
}
