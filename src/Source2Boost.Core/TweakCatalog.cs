using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Каталог всех твиков Source2Boost. Здесь — единый источник правды,
/// из которого строятся профили и чек-лист UI. Всё трёхъязычно.
/// </summary>
public static class TweakCatalog
{
    /// <summary>
    /// Старые реестровые NVIDIA-твики (nvidia-max-perf / nvidia-low-latency) делают ровно то же,
    /// что твик «Игровой профиль драйвера NVIDIA» через NVAPI — это дубль в списке. Показываем
    /// их ТОЛЬКО как запасной вариант: если NVAPI недоступен (нестандартный/старый драйвер)
    /// ИЛИ если пользователь их уже применил раньше — иначе он не смог бы их откатить.
    /// </summary>
    private static bool LegacyNvidiaVisible(TweakContext ctx, string path, string name, int appliedValue)
        => ctx.Hardware.IsNvidia && (!NvApi.IsAvailable() || RegDwordIs(path, name, appliedValue));

    private static bool RegDwordIs(string path, string name, int value)
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                     .OpenSubKey(path);
            return k?.GetValue(name) is int v && v == value;
        }
        catch { return false; }
    }

    private const string MmProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string KernelKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string MemoryManagement = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    private const string NvidiaClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
    private const string GpuPrefs = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string Cs2Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe\PerfOptions";
    private const string NvlddmkmKey = @"SYSTEM\CurrentControlSet\Services\nvlddmkm";
    private const string HvciKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string PowerThrottlingKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
    private const string DwmKey = @"SOFTWARE\Microsoft\Windows\Dwm";
    private const string BackgroundAppsKey = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";
    private const string ControlKey = @"SYSTEM\CurrentControlSet\Control";
    private const string PrefetchKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
    private const string QosCs2Key = @"SOFTWARE\Policies\Microsoft\Windows\QoS\Source2Boost-CS2";

    public static IReadOnlyList<ITweak> All() => _all ??= Build();
    private static IReadOnlyList<ITweak>? _all;

    private static IReadOnlyList<ITweak> Build()
    {
        var cs2Exe = Cs2Paths.Cs2ExePath();

        return new ITweak[]
        {
            // ---- Фреймтайм / латентность (Safe) ----
            new RegistryTweak(
                "mmcss-games", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("Приоритет игр MMCSS", "Пріоритет ігор MMCSS", "MMCSS games priority"),
                new L10n("Планировщик мультимедиа отдаёт игре больше GPU/CPU-времени.",
                         "Планувальник мультимедіа віддає грі більше GPU/CPU-часу.",
                         "The multimedia scheduler gives the game more GPU/CPU time."),
                new L10n("-латентность", "-латентність", "-latency"),
                RegistryHive.LocalMachine, $@"{MmProfile}\Tasks\Games",
                new[]
                {
                    new RegEntry("GPU Priority", RegistryValueKind.DWord, 8),
                    new RegEntry("Priority", RegistryValueKind.DWord, 6),
                    new RegEntry("Scheduling Category", RegistryValueKind.String, "High"),
                    new RegEntry("SFIO Priority", RegistryValueKind.String, "High"),
                }),

            new RegistryTweak(
                "system-responsiveness", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("Убрать резерв под фон", "Прибрати резерв під фон", "Kill background reservation"),
                new L10n("Планировщик Windows перестаёт резервировать время под фоновые задачи и отдаёт его игре — меньше рывков.",
                         "Планувальник Windows перестає резервувати час під фонові задачі й віддає його грі — менше ривків.",
                         "Windows stops reserving time for background tasks and gives it to the game — fewer hitches."),
                new L10n("+ровность", "+рівність", "+smoothness"),
                RegistryHive.LocalMachine, MmProfile,
                new[]
                {
                    new RegEntry("SystemResponsiveness", RegistryValueKind.DWord, 0),
                }),

            new RegistryTweak(
                "game-mode", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("Игровой режим Windows", "Ігровий режим Windows", "Windows Game Mode"),
                new L10n("Windows приоритезирует активную игру.",
                         "Windows пріоритезує активну гру.",
                         "Windows prioritizes the active game."),
                new L10n("+FPS", "+FPS", "+FPS"),
                RegistryHive.CurrentUser, @"Software\Microsoft\GameBar",
                new[] { new RegEntry("AutoGameModeEnabled", RegistryValueKind.DWord, 1) }),

            new RegistryTweak(
                "mouse-accel-off", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("Отключить акселерацию мыши", "Вимкнути акселерацію миші", "Disable mouse acceleration"),
                new L10n("Выключает «повышенную точность указателя» — курсор двигается ровно 1:1 к мыши, важно для стабильного прицела.",
                         "Вимикає «підвищену точність указівника» — курсор рухається рівно 1:1 до миші, важливо для стабільного прицілу.",
                         "Turns off 'enhance pointer precision' — the cursor tracks the mouse exactly 1:1, key for consistent aim."),
                new L10n("+точность прицела", "+точність прицілу", "+aim consistency"),
                RegistryHive.CurrentUser, @"Control Panel\Mouse",
                new[]
                {
                    new RegEntry("MouseSpeed", RegistryValueKind.String, "0"),
                    new RegEntry("MouseThreshold1", RegistryValueKind.String, "0"),
                    new RegEntry("MouseThreshold2", RegistryValueKind.String, "0"),
                }),

            new RegistryTweak(
                "visual-fx-performance", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("Эффекты Windows: производительность", "Ефекти Windows: продуктивність", "Windows effects: performance"),
                new L10n("Ставит эффекты интерфейса Windows в режим «максимум быстродействия» — меньше нагрузки на видеокарту и рабочий стол.",
                         "Ставить ефекти інтерфейсу Windows у режим «максимум швидкодії» — менше навантаження на відеокарту й робочий стіл.",
                         "Sets Windows interface effects to 'best performance' — less load on the GPU and desktop."),
                new L10n("-нагрузка", "-навантаження", "-overhead"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                new[] { new RegEntry("VisualFXSetting", RegistryValueKind.DWord, 2) }),

            new RegistryTweak(
                "fso-disable-cs2", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("Отключить FSO для cs2.exe", "Вимкнути FSO для cs2.exe", "Disable FSO for cs2.exe"),
                new L10n("Отключает полноэкранные оптимизации Windows для CS2 — стабильнее кадры в полноэкранном режиме.",
                         "Вимикає повноекранні оптимізації Windows для CS2 — стабільніші кадри у повноекранному режимі.",
                         "Turns off Windows fullscreen optimizations for CS2 — steadier frames in fullscreen."),
                new L10n("+ровность", "+рівність", "+smoothness"),
                RegistryHive.CurrentUser, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers",
                new[] { new RegEntry(cs2Exe ?? "cs2.exe", RegistryValueKind.String, "~ DISABLEDXMAXIMIZEDWINDOWEDMODE") },
                supported: _ => cs2Exe is not null),

            new RegistryTweak(
                "gpu-preference-cs2", TweakCategory.Frametime, RiskLevel.Safe,
                new L10n("CS2 на дискретную GPU (High Performance)", "CS2 на дискретну GPU (High Performance)", "CS2 on discrete GPU (High Performance)"),
                new L10n("Windows форсирует для cs2.exe дискретную видеокарту в режиме максимальной производительности (не встройку).",
                         "Windows форсує для cs2.exe дискретну відеокарту в режимі максимальної продуктивності (не вбудовану).",
                         "Windows forces cs2.exe onto the discrete GPU in high-performance mode (not the iGPU)."),
                new L10n("+гарантия дискретной GPU", "+гарантія дискретної GPU", "+guaranteed dGPU"),
                RegistryHive.CurrentUser, GpuPrefs,
                new[] { new RegEntry(cs2Exe ?? "cs2.exe", RegistryValueKind.String, "GpuPreference=2;") },
                supported: _ => cs2Exe is not null),

            // ПРИМ.: ShaderCacheTweak намеренно УБРАН из авто-каталога/профилей.
            // Постоянное стирание шейдеров вредно (дольше загрузка + стуттер рекомпиляции),
            // поэтому ежедневный таймер удалён. Класс сохранён для ручной кнопки
            // (ShaderCacheTweak.CleanNow()); старую вредную задачу сносит StandbyCleanTweak.Apply.

            // ---- Память (Safe) — анти-стуттер вместо shader-таймера ----
            // Периодическая очистка standby-списка памяти каждые 5 минут (ISLC-подобно).
            new StandbyCleanTweak(),

            // ---- Службы (Safe) ----
            new RegistryTweak(
                "gamedvr-off", TweakCategory.Services, RiskLevel.Safe,
                new L10n("Отключить Xbox DVR", "Вимкнути Xbox DVR", "Disable Xbox DVR"),
                new L10n("Фоновая запись игры перестаёт есть ресурсы.",
                         "Фоновий запис гри перестає їсти ресурси.",
                         "Background game recording stops eating resources."),
                new L10n("-нагрузка", "-навантаження", "-overhead"),
                RegistryHive.CurrentUser, @"System\GameConfigStore",
                new[] { new RegEntry("GameDVR_Enabled", RegistryValueKind.DWord, 0) }),

            // ---- Фоновые приложения (Safe) ----
            new RegistryTweak(
                "background-apps-off", TweakCategory.Services, RiskLevel.Safe,
                new L10n("Отключить фоновые приложения", "Вимкнути фонові застосунки", "Disable background apps"),
                new L10n("UWP/Store-приложения (Погода, Xbox, виджеты и т.п.) перестают крутиться в фоне и есть CPU/RAM во время игры. Обратимо.",
                         "UWP/Store-застосунки (Погода, Xbox, віджети тощо) перестають крутитися у фоні та їсти CPU/RAM під час гри. Оборотно.",
                         "UWP/Store apps (Weather, Xbox, widgets, etc.) stop running in the background and eating CPU/RAM while gaming. Reversible."),
                new L10n("-фоновая нагрузка", "-фонове навантаження", "-background load"),
                RegistryHive.CurrentUser, BackgroundAppsKey,
                new[] { new RegEntry("GlobalUserDisabled", RegistryValueKind.DWord, 1) }),

            // ---- CPU / питание (Medium) ----
            new RegistryTweak(
                "win32-priority", TweakCategory.CpuPower, RiskLevel.Medium,
                new L10n("Приоритет переднего плана", "Пріоритет переднього плану", "Foreground priority boost"),
                new L10n("Отдаёт активной игре больше времени процессора, чем фоновым программам — выше отзывчивость.",
                         "Віддає активній грі більше часу процесора, ніж фоновим програмам — вищий відгук.",
                         "Gives the active game more CPU time than background programs — snappier response."),
                new L10n("+отзывчивость", "+відгук", "+responsiveness"),
                RegistryHive.LocalMachine, PriorityControl,
                new[] { new RegEntry("Win32PrioritySeparation", RegistryValueKind.DWord, 0x26) }),

            new RegistryTweak(
                "cs2-high-priority", TweakCategory.CpuPower, RiskLevel.Medium,
                new L10n("Постоянный высокий приоритет cs2.exe", "Постійний високий пріоритет cs2.exe", "Persistent high priority for cs2.exe"),
                new L10n("CS2 всегда запускается с высоким приоритетом процессора — надёжнее, чем -high в параметрах (работает всегда). Помогает, когда упор в процессор.",
                         "CS2 завжди запускається з високим пріоритетом процесора — надійніше, ніж -high у параметрах (працює завжди). Допомагає, коли впор у процесор.",
                         "CS2 always launches at high CPU priority — more reliable than -high in launch options (always applied). Helps when you're CPU-bound."),
                new L10n("+приоритет CPU", "+пріоритет CPU", "+CPU priority"),
                RegistryHive.LocalMachine, Cs2Ifeo,
                new[] { new RegEntry("CpuPriorityClass", RegistryValueKind.DWord, 3) }),

            // Аффинити CS2 по топологии CPU — виден только на гибридных Intel / много-CCD AMD.
            new Cs2AffinityTweak(),

            new PowerPlanTweak(),

            // ---- Фреймтайм (Medium, перезапуск) ----
            new RegistryTweak(
                "timer-resolution-global", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Глобальный таймер разрешения", "Глобальний таймер роздільності", "Global timer resolution"),
                new L10n("Заставляет Windows держать точный системный таймер даже в полноэкранных играх — меньше микрофризов. (Win10 2004+/Win11.)",
                         "Змушує Windows тримати точний системний таймер навіть у повноекранних іграх — менше мікрофризів. (Win10 2004+/Win11.)",
                         "Makes Windows keep a precise system timer even in fullscreen games — fewer micro-stutters. (Win10 2004+/Win11.)"),
                new L10n("-микрофризы", "-мікрофризи", "-micro-stutter"),
                RegistryHive.LocalMachine, KernelKey,
                new[] { new RegEntry("GlobalTimerResolutionRequests", RegistryValueKind.DWord, 1) },
                requiresRestart: true),

            new RegistryTweak(
                "gpu-hags", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Аппаратное планирование GPU", "Апаратне планування GPU", "Hardware-accelerated GPU scheduling"),
                new L10n("Включает аппаратное планирование GPU: видеокарта сама управляет очередью кадров — ниже задержка их подачи.",
                         "Вмикає апаратне планування GPU: відеокарта сама керує чергою кадрів — нижча затримка їх подачі.",
                         "Enables hardware GPU scheduling: the card manages its own frame queue — lower submission latency."),
                new L10n("-латентность", "-латентність", "-latency"),
                RegistryHive.LocalMachine, GraphicsDrivers,
                new[] { new RegEntry("HwSchMode", RegistryValueKind.DWord, 2) },
                requiresRestart: true),

            // ---- Память: фиксированный файл подкачки (Medium, только ≤8 ГБ RAM) ----
            new PagefileFixedTweak(),

            // ---- Память: Prefetch/Superfetch off (Medium) ----
            new RegistryTweak(
                "prefetch-off", TweakCategory.Memory, RiskLevel.Medium,
                new L10n("Отключить Prefetch/Superfetch", "Вимкнути Prefetch/Superfetch", "Disable Prefetch/Superfetch"),
                new L10n("Windows перестаёт заранее подгружать и «прогревать» кэш с диска в фоне. На SSD это бесполезно, зато убирает лишние обращения к диску и памяти. Работает в паре с отключённой службой SysMain. Обратимо.",
                         "Windows перестає заздалегідь підвантажувати та «прогрівати» кеш з диска у фоні. На SSD це марно, зате прибирає зайві звернення до диска й пам'яті. Працює в парі з вимкненою службою SysMain. Оборотно.",
                         "Windows stops pre-loading and 'warming' cache from disk in the background. Useless on an SSD, and it removes needless disk/RAM churn. Pairs with the disabled SysMain service. Reversible."),
                new L10n("-фоновые обращения к диску", "-фонові звернення до диска", "-background disk churn"),
                RegistryHive.LocalMachine, PrefetchKey,
                new[]
                {
                    new RegEntry("EnablePrefetcher", RegistryValueKind.DWord, 0),
                    new RegEntry("EnableSuperfetch", RegistryValueKind.DWord, 0),
                }),

            // ---- Сеть: QoS DSCP для cs2 (Medium) ----
            new RegistryTweak(
                "qos-cs2", TweakCategory.Network, RiskLevel.Medium,
                new L10n("Приоритет трафика CS2 (QoS)", "Пріоритет трафіку CS2 (QoS)", "CS2 traffic priority (QoS)"),
                new L10n("Помечает сетевые пакеты CS2 как приоритетные. Роутер и провайдер, если поддерживают, пропускают их первыми — ровнее пинг под нагрузкой. Обратимо; эффект после перезагрузки.",
                         "Позначає мережеві пакети CS2 як пріоритетні. Роутер і провайдер, якщо підтримують, пропускають їх першими — рівніший пінг під навантаженням. Оборотно; ефект після перезавантаження.",
                         "Marks CS2's network packets as high-priority. Routers and ISPs that support it pass them first — steadier ping under load. Reversible; effect after a reboot."),
                new L10n("+приоритет пакетов", "+пріоритет пакетів", "+packet priority"),
                RegistryHive.LocalMachine, QosCs2Key,
                new[]
                {
                    new RegEntry("Version", RegistryValueKind.String, "1.0"),
                    new RegEntry("Application Name", RegistryValueKind.String, "cs2.exe"),
                    new RegEntry("Protocol", RegistryValueKind.String, "*"),
                    new RegEntry("Local Port", RegistryValueKind.String, "*"),
                    new RegEntry("Local IP", RegistryValueKind.String, "*"),
                    new RegEntry("Local IP Prefix Length", RegistryValueKind.String, "*"),
                    new RegEntry("Remote Port", RegistryValueKind.String, "*"),
                    new RegEntry("Remote IP", RegistryValueKind.String, "*"),
                    new RegEntry("Remote IP Prefix Length", RegistryValueKind.String, "*"),
                    new RegEntry("DSCP Value", RegistryValueKind.String, "46"),
                    new RegEntry("Throttle Rate", RegistryValueKind.String, "-1"),
                },
                requiresRestart: true),

            // ---- Сеть (Medium) ----
            new RegistryTweak(
                "network-throttling-off", TweakCategory.Network, RiskLevel.Medium,
                new L10n("Снять сетевой троттлинг", "Зняти мережевий тротлінг", "Disable network throttling"),
                new L10n("Снимает встроенное ограничение сетевых пакетов, которое Windows держит ради мультимедиа — ниже сетевая задержка.",
                         "Знімає вбудоване обмеження мережевих пакетів, яке Windows тримає заради мультимедіа — нижча мережева затримка.",
                         "Removes the built-in network packet limit Windows keeps for multimedia — lower network latency."),
                new L10n("-сетевая латентность", "-мережева латентність", "-network latency"),
                RegistryHive.LocalMachine, MmProfile,
                new[] { new RegEntry("NetworkThrottlingIndex", RegistryValueKind.DWord, unchecked((int)0xFFFFFFFF)) }),

            // ---- Память / службы (Medium) ----
            // Живая остановка SysMain+DiagTrack: заменяет прежние чисто-реестровые
            // sysmain-off / diagtrack-off (тип запуска + немедленный stop без перезагрузки).
            LiveServiceStopTweak.SysMainDiagTrack(),

            // Доп. службы (паритет с cs2-omz): индексатор поиска + очередь печати.
            LiveServiceStopTweak.SearchAndSpooler(),

            // Службы Xbox (паритет с CS2-Ultimate-Optimization): не нужны для CS2 (не из Store).
            LiveServiceStopTweak.XboxServices(),

            // ---- Планировщик питания процессов (Medium) ----
            new RegistryTweak(
                "power-throttling-off", TweakCategory.CpuPower, RiskLevel.Medium,
                new L10n("Отключить Power Throttling", "Вимкнути Power Throttling", "Disable Power Throttling"),
                new L10n("Windows перестаёт занижать частоту процессора ради энергосбережения. На настольном ПК это лишнее ограничение. Обратимо.",
                         "Windows перестає занижати частоту процесора заради енергозбереження. На настільному ПК це зайве обмеження. Оборотно.",
                         "Windows stops lowering CPU frequency to save power. Pointless on a desktop. Reversible."),
                new L10n("+частота CPU", "+частота CPU", "+CPU clocks"),
                RegistryHive.LocalMachine, PowerThrottlingKey,
                new[] { new RegEntry("PowerThrottlingOff", RegistryValueKind.DWord, 1) }),

            // ---- DWM / MPO (Medium, анти-стуттер) ----
            new RegistryTweak(
                "mpo-off", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Отключить MPO (анти-стуттер)", "Вимкнути MPO (анти-стуттер)", "Disable MPO (anti-stutter)"),
                new L10n("Отключает Multi-Plane Overlay — частую причину мерцания и микрофризов рабочего стола и игры на драйверах NVIDIA. Обратимо.",
                         "Вимикає Multi-Plane Overlay — часту причину мерехтіння та мікрофризів робочого столу й гри на драйверах NVIDIA. Оборотно.",
                         "Turns off Multi-Plane Overlay — a common cause of desktop/game flicker and micro-stutter on NVIDIA drivers. Reversible."),
                new L10n("-микрофризы", "-мікрофризи", "-micro-stutter"),
                RegistryHive.LocalMachine, DwmKey,
                new[] { new RegEntry("OverlayTestMode", RegistryValueKind.DWord, 5) }),

            // ---- svchost на слабой RAM (Medium, перезапуск) ----
            new RegistryTweak(
                "svchost-group", TweakCategory.Memory, RiskLevel.Medium,
                new L10n("Группировать службы svchost", "Групувати служби svchost", "Group svchost services"),
                new L10n("Windows снова объединяет системные службы в общие процессы вместо отдельного на каждую. На 8 ГБ RAM заметно меньше процессов и расхода памяти. Обратимо, нужна перезагрузка.",
                         "Windows знову об'єднує системні служби у спільні процеси замість окремого на кожну. На 8 ГБ RAM помітно менше процесів і витрати пам'яті. Оборотно, потрібне перезавантаження.",
                         "Windows groups system services back into shared processes instead of one each. On 8 GB machines, noticeably fewer processes and less RAM use. Reversible, needs a reboot."),
                new L10n("-расход RAM", "-витрата RAM", "-RAM use"),
                RegistryHive.LocalMachine, ControlKey,
                new[] { new RegEntry("SvcHostSplitThresholdInKB", RegistryValueKind.DWord, unchecked((int)0x4000000)) },
                requiresRestart: true),

            // ---- Память / ядро (Medium, перезапуск) ----
            new RegistryTweak(
                "disable-paging-executive", TweakCategory.Memory, RiskLevel.Medium,
                new L10n("Держать ядро в RAM", "Тримати ядро в RAM", "Keep kernel in RAM"),
                new L10n("Держит код ядра и драйверы в оперативной памяти, а не в файле подкачки — меньше дисковых задержек. Нужно 8 ГБ+ RAM.",
                         "Тримає код ядра й драйвери в оперативній пам'яті, а не у файлі підкачки — менше дискових затримок. Потрібно 8 ГБ+ RAM.",
                         "Keeps kernel and driver code in RAM instead of the pagefile — fewer disk stalls. Needs 8 GB+ RAM."),
                new L10n("-дисковые задержки", "-дискові затримки", "-disk stalls"),
                RegistryHive.LocalMachine, MemoryManagement,
                new[] { new RegEntry("DisablePagingExecutive", RegistryValueKind.DWord, 1) },
                requiresRestart: true),

            // ---- Память (Medium, перезапуск) — метод про-тюнеров ----
            // Отключить сжатие памяти: Windows не тратит CPU на сжатие страниц RAM.
            new MemoryCompressionTweak(),

            // ---- NVIDIA (Medium, перезапуск) ----
            new RegistryTweak(
                "nvidia-max-perf", TweakCategory.Nvidia, RiskLevel.Medium,
                new L10n("NVIDIA: максимальная производительность", "NVIDIA: максимальна продуктивність", "NVIDIA: prefer max performance"),
                new L10n("Заставляет видеокарту NVIDIA держать максимальную частоту и не сбрасывать её в простое — нет провалов кадра при нагрузке. Обратимо.",
                         "Змушує відеокарту NVIDIA тримати максимальну частоту й не скидати її в простої — немає провалів кадру під навантаженням. Оборотно.",
                         "Makes the NVIDIA GPU hold its max clock and stop down-clocking at idle — no frame dips under load. Reversible."),
                new L10n("+стабильная частота GPU", "+стабільна частота GPU", "+steady GPU clock"),
                RegistryHive.LocalMachine, NvidiaClassKey,
                new[]
                {
                    new RegEntry("PowerMizerEnable", RegistryValueKind.DWord, 1),
                    new RegEntry("PowerMizerLevel", RegistryValueKind.DWord, 1),
                    new RegEntry("PowerMizerLevelAC", RegistryValueKind.DWord, 1),
                    new RegEntry("PerfLevelSrc", RegistryValueKind.DWord, 0x2222),
                },
                requiresRestart: true,
                supported: ctx => LegacyNvidiaVisible(ctx, NvidiaClassKey, "PerfLevelSrc", 0x2222)),

            // ---- NVIDIA / латентность (Medium, перезапуск) — паритет с CS2-Ultimate-Optimization ----
            new RegistryTweak(
                "nvidia-low-latency", TweakCategory.Nvidia, RiskLevel.Medium,
                new L10n("NVIDIA: режим низкой латентности", "NVIDIA: режим низької латентності", "NVIDIA: low-latency mode"),
                new L10n("Драйвер NVIDIA держит очередь кадров короче (как «Режим низкой задержки» в панели) — ниже задержка ввода. Обратимо, нужна перезагрузка.",
                         "Драйвер NVIDIA тримає чергу кадрів коротшою (як «Режим низької затримки» в панелі) — нижча затримка вводу. Оборотно, потрібне перезавантаження.",
                         "The NVIDIA driver keeps the frame queue shorter (like 'Low Latency Mode' in the panel) — lower input lag. Reversible, needs a reboot."),
                new L10n("-задержка ввода", "-затримка вводу", "-input lag"),
                RegistryHive.LocalMachine, NvlddmkmKey,
                new[] { new RegEntry("EnableLowLatencyMode", RegistryValueKind.DWord, 1) },
                requiresRestart: true,
                supported: ctx => LegacyNvidiaVisible(ctx, NvlddmkmKey, "EnableLowLatencyMode", 1)),

            // ---- NVIDIA: игровой профиль драйвера через NVAPI (Medium) ----
            new NvApiProfileTweak(),

            // ---- AMD Radeon (Medium, перезапуск) — аналог PowerMizer для AMD ----
            new RegistryTweak(
                "amd-radeon-max", TweakCategory.Nvidia, RiskLevel.Medium,
                new L10n("AMD Radeon: без сброса частот", "AMD Radeon: без скидання частот", "AMD Radeon: no downclock"),
                new L10n("Отключает ультра-энергосбережение Radeon: видеокарта не роняет частоту в простое — нет провалов кадра при нагрузке. Обратимо, нужна перезагрузка.",
                         "Вимикає ультра-енергозбереження Radeon: відеокарта не роняє частоту в простої — немає провалів кадру під навантаженням. Оборотно, потрібне перезавантаження.",
                         "Disables Radeon's ultra low-power state: the GPU stops dropping clocks at idle — no frame dips under load. Reversible, needs a reboot."),
                new L10n("+стабильная частота GPU", "+стабільна частота GPU", "+steady GPU clock"),
                RegistryHive.LocalMachine, NvidiaClassKey, // тот же класс дисплейных адаптеров \0000
                new[] { new RegEntry("EnableUlps", RegistryValueKind.DWord, 0) },
                requiresRestart: true,
                supported: ctx => ctx.Hardware.IsAmd),

            // ---- CPU питание (Medium) ----
            new CoreParkingTweak(),

            // ---- Windows / Defender (Medium) ----
            new DefenderExclusionTweak(),

            // ---- Деблоат (Medium, обратимый, Defender не трогает) ----
            // Место в списке важно: карточки идут от слабого воздействия к сильному,
            // а раньше этот Medium-твик стоял в самом низу — после Extreme.
            new DebloatTweak(),

            // ---- CPU / митигации (High, перезапуск) — самый жирный выигрыш на Skylake ----
            new RegistryTweak(
                "spectre-off", TweakCategory.CpuPower, RiskLevel.High,
                new L10n("Отключить митигации Spectre/Meltdown", "Вимкнути мітигації Spectre/Meltdown", "Disable Spectre/Meltdown mitigations"),
                new L10n("🔴 Снимает защитные заплатки процессора от Spectre/Meltdown. Возвращает 5–15% мощности CPU — сразу в FPS и 1% low, особенно на старых чипах (Skylake, i7-6700). Минус: чуть слабее защита от редких спекулятивных атак — включай осознанно. Полностью обратимо, нужна перезагрузка.",
                         "🔴 Знімає захисні латки процесора від Spectre/Meltdown. Повертає 5–15% потужності CPU — одразу у FPS та 1% low, особливо на старих чипах (Skylake, i7-6700). Мінус: трохи слабший захист від рідкісних атак — вмикай свідомо. Повністю оборотно, потрібне перезавантаження.",
                         "🔴 Removes the CPU's Spectre/Meltdown security patches. Recovers 5–15% CPU power — straight into FPS and 1% lows, especially on older chips (Skylake, i7-6700). Downside: slightly weaker protection against rare attacks — enable knowingly. Fully reversible, needs a reboot."),
                new L10n("+5–15% CPU (−защита)", "+5–15% CPU (−захист)", "+5–15% CPU (−security)"),
                RegistryHive.LocalMachine, MemoryManagement,
                new[]
                {
                    new RegEntry("FeatureSettingsOverride", RegistryValueKind.DWord, 3),
                    new RegEntry("FeatureSettingsOverrideMask", RegistryValueKind.DWord, 3),
                },
                requiresRestart: true),

            // ---- Безопасность / виртуализация (High, перезапуск) — до −25% FPS на слабом CPU ----
            new RegistryTweak(
                "hvci-off", TweakCategory.CpuPower, RiskLevel.High,
                new L10n("Отключить изоляцию ядра (VBS/HVCI)", "Вимкнути ізоляцію ядра (VBS/HVCI)", "Disable Core Isolation (VBS/HVCI)"),
                new L10n("🔴 Выключает «Целостность памяти» (изоляцию ядра). Она работает через виртуализацию и на слабом CPU отнимает до 10–25% FPS. Минус: слабее защита от драйверных атак — включай осознанно. Обратимо, нужна перезагрузка. Если изоляция уже выключена — эффекта нет.",
                         "🔴 Вимикає «Цілісність пам'яті» (ізоляцію ядра). Вона працює через віртуалізацію і на слабкому CPU відбирає до 10–25% FPS. Мінус: слабший захист від драйверних атак — вмикай свідомо. Оборотно, потрібне перезавантаження. Якщо ізоляція вже вимкнена — ефекту немає.",
                         "🔴 Turns off Memory Integrity (Core Isolation). It runs through virtualization and costs up to 10–25% FPS on a weak CPU. Downside: weaker protection against driver attacks — enable knowingly. Reversible, needs a reboot. No effect if already off."),
                new L10n("+10–25% FPS (−защита)", "+10–25% FPS (−захист)", "+10–25% FPS (−security)"),
                RegistryHive.LocalMachine, HvciKey,
                new[] { new RegEntry("Enabled", RegistryValueKind.DWord, 0) },
                requiresRestart: true),

            // ---- CPU / таймеры загрузчика (High, перезапуск) ----
            new BcdEditTweak(),

            // ---- NVIDIA / прерывания (High, перезапуск) ----
            new MsiModeTweak(),

            // ---- Прерывания GPU мимо CPU0 (High, перезапуск) — дискретная GPU + многоядерный CPU ----
            new InterruptAffinityTweak(),

            // ---- Сеть (High) ----
            new NagleTweak(),

            // ---- CPU / простой (High) — постоянная частота ----
            new CpuIdleDisableTweak(),

            // ==== БЕЗБАШЕННОЕ (Extreme) — НЕ входит в профили, только вручную ====
            new DefenderRealtimeOffTweak(),
        };
    }

    /// <summary>
    /// Твики профиля, отсортированные по возрастанию риска (сначала безопасные).
    /// Безопасный — только Safe; Оптимальный — Safe+Medium; Максимум — всё, включая High.
    /// </summary>
    public static IEnumerable<ITweak> ForProfile(Profile p)
    {
        // «Скоро»-заглушки (IComingSoon) не входят ни в один профиль.
        var byRisk = All().Where(t => t is not IComingSoon).OrderBy(t => (int)t.Risk);
        return p switch
        {
            Profile.Safe => byRisk.Where(t => t.Risk == RiskLevel.Safe),
            Profile.Optimal => byRisk.Where(t => t.Risk is RiskLevel.Safe or RiskLevel.Medium),
            // Максимум — всё, КРОМЕ «безбашенных» Extreme (те только вручную в списке твиков).
            _ => byRisk.Where(t => t.Risk != RiskLevel.Extreme),
        };
    }

    /// <summary>
    /// То же, что <see cref="ForProfile(Profile)"/>, но без откровенно несовместимых
    /// с железом твиков (например AMD-твик на NVIDIA) — счётчик плана совпадает с тем,
    /// что реально применится.
    /// </summary>
    public static IEnumerable<ITweak> ForProfile(Profile p, TweakContext ctx)
        => ForProfile(p).Where(t => { try { return t.IsSupported(ctx); } catch { return true; } });
}
