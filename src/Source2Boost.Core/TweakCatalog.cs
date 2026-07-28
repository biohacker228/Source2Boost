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
            // Постоянное стирание шейдеров вредно (дольше загрузка + статтер рекомпиляции),
            // поэтому ежедневный таймер удалён. Класс сохранён для ручной кнопки
            // (ShaderCacheTweak.CleanNow()); старую вредную задачу сносит StandbyCleanTweak.Apply.

            // ---- Память (Safe) — анти-статтер вместо shader-таймера ----
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

            // (HAGS убран из форсируемых дефолтов — он спорный и железозависимый; перенесён в
            //  Лабораторию как A/B-эксперимент «отключить HAGS», см. gpu-hags-off ниже.)

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

            // ---- DWM / MPO (Medium, анти-статтер) ----
            new RegistryTweak(
                "mpo-off", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Отключить MPO (анти-статтер)", "Вимкнути MPO (анти-статтер)", "Disable MPO (anti-stutter)"),
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

            // ==== ЛАБОРАТОРИЯ (эксперимент) — НЕ входит в профили/оценку, только вручную с A/B ====
            // Распределение истечения таймеров по всем ядрам, а не только по CPU0. На многоядерных
            // CPU теоретически ровнее фреймтайм, но выигрыш НЕ гарантирован — кандидат на замер.
            new ExperimentalTweak(new RegistryTweak(
                "distribute-timers", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Распределять таймеры по ядрам", "Розподіляти таймери по ядрах", "Distribute timers across cores"),
                new L10n("Windows распределяет истечение системных таймеров по всем ядрам, а не только по нулевому — на многоядерном CPU теоретически ровнее фреймтайм. Польза НЕ гарантирована: замерь бенчмарком до/после. Полностью обратимо, нужна перезагрузка.",
                         "Windows розподіляє спливання системних таймерів по всіх ядрах, а не лише по нульовому — на багатоядерному CPU теоретично рівніший фреймтайм. Користь НЕ гарантована: зміряй бенчмарком до/після. Повністю оборотно, потрібне перезавантаження.",
                         "Windows spreads system-timer expiration across all cores instead of only core 0 — on a multi-core CPU this may smooth frametime. Benefit is NOT guaranteed: measure with the benchmark before/after. Fully reversible, needs a reboot."),
                new L10n("?ровность (замерь)", "?рівність (зміряй)", "?smoothness (measure)"),
                RegistryHive.LocalMachine, KernelKey,
                new[] { new RegEntry("DistributeTimers", RegistryValueKind.DWord, 1) },
                requiresRestart: true,
                supported: ctx => ctx.Hardware.CpuThreads >= 4)),   // смысл только на многоядерном CPU

            // Больший тайм-аут восстановления GPU (TDR). Под тяжёлой нагрузкой реже «дёргает»
            // сбросом драйвера. НЕ поднимает средний FPS — только про редкие жёсткие хитчи.
            // Только при дискретной GPU. Кандидат на замер.
            new ExperimentalTweak(new RegistryTweak(
                "tdr-delay", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Увеличить тайм-аут GPU (TDR)", "Збільшити тайм-аут GPU (TDR)", "Raise GPU timeout (TDR)"),
                new L10n("Поднимает тайм-аут восстановления драйвера GPU (TdrDelay 2→10 сек) — под тяжёлой нагрузкой Windows реже роняет драйвер и реже даёт жёсткий хитч. Средний FPS НЕ растёт. Минус: при реальном зависании GPU экран дольше не восстановится. Обратимо, нужна перезагрузка. Замерь до/после.",
                         "Піднімає тайм-аут відновлення драйвера GPU (TdrDelay 2→10 с) — під важким навантаженням Windows рідше роняє драйвер і рідше дає жорсткий хітч. Середній FPS НЕ зростає. Мінус: при реальному зависанні GPU екран довше не відновиться. Оборотно, потрібне перезавантаження. Зміряй до/після.",
                         "Raises the GPU driver recovery timeout (TdrDelay 2→10 s) — under heavy load Windows resets the driver less often, so fewer hard hitches. Average FPS does NOT rise. Downside: on a real GPU hang the screen stays frozen longer. Reversible, needs a reboot. Measure before/after."),
                new L10n("?меньше жёстких хитчей", "?менше жорстких хітчів", "?fewer hard hitches"),
                RegistryHive.LocalMachine, GraphicsDrivers,
                new[] { new RegEntry("TdrDelay", RegistryValueKind.DWord, 10) },
                requiresRestart: true,
                supported: ctx => ctx.Hardware.HasDiscreteGpu)),

            // HAGS (аппаратное планирование GPU) — СПОРНЫЙ и ЖЕЛЕЗОЗАВИСИМЫЙ. На соревновательной
            // CS2, особенно на старых GPU / Windows 10 (наша ЦА), ВЫКЛючение часто даёт ровнее
            // фреймтайм и меньше статтера; на новых GPU / Windows 11 иногда лучше ВКЛючённым.
            // Поэтому в профилях не форсим — даём проверить замером именно выключение.
            new ExperimentalTweak(new RegistryTweak(
                "gpu-hags-off", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Отключить аппаратное планирование GPU", "Вимкнути апаратне планування GPU", "Disable hardware GPU scheduling"),
                new L10n("Выключает HAGS (HwSchMode=1). На соревновательной CS2, особенно на старых видеокартах и Windows 10, это часто убирает микро-статтер и делает фреймтайм ровнее — GPU перестаёт сам управлять очередью кадров. Но эффект ЖЕЛЕЗОЗАВИСИМЫЙ: на новых GPU и Windows 11 иногда лучше наоборот, с включённым. Строго под замер до/после. Обратимо, нужна перезагрузка.",
                         "Вимикає HAGS (HwSchMode=1). На змагальній CS2, особливо на старих відеокартах і Windows 10, це часто прибирає мікро-статтер і робить фреймтайм рівнішим — GPU перестає сам керувати чергою кадрів. Але ефект ЗАЛІЗОЗАЛЕЖНИЙ: на нових GPU і Windows 11 іноді краще навпаки. Строго під замір до/після. Оборотно, потрібне перезавантаження.",
                         "Turns HAGS off (HwSchMode=1). In competitive CS2, especially on older GPUs and Windows 10, this often removes micro-stutter and smooths frametime — the GPU no longer manages its own frame queue. But the effect is HARDWARE-DEPENDENT: on newer GPUs and Windows 11 leaving it on is sometimes better. Measure before/after. Reversible, needs a reboot."),
                new L10n("?ровность (железозависимо)", "?рівність (залізозалежно)", "?smoothness (hardware-dependent)"),
                RegistryHive.LocalMachine, GraphicsDrivers,
                new[] { new RegEntry("HwSchMode", RegistryValueKind.DWord, 1) },
                requiresRestart: true)),

            // Классический «латентный» твик приоритета RTC (IRQ8). На современной Windows часто
            // ПЛАЦЕБО — включён в Лабораторию именно чтобы дать это проверить/опровергнуть замером.
            new ExperimentalTweak(new RegistryTweak(
                "irq8-priority", TweakCategory.CpuPower, RiskLevel.Medium,
                new L10n("Приоритет RTC-таймера (IRQ8)", "Пріоритет RTC-таймера (IRQ8)", "RTC timer priority (IRQ8)"),
                new L10n("Повышает приоритет прерывания системных часов реального времени (IRQ8). Классический твик «за низкую задержку», но на современной Windows часто НИЧЕГО не даёт (плацебо). Добавлен, чтобы ты проверил это на своём железе бенчмарком, а не верил на слово. Обратимо, нужна перезагрузка.",
                         "Підвищує пріоритет переривання системного годинника реального часу (IRQ8). Класичний твік «за низьку затримку», але на сучасній Windows часто НІЧОГО не дає (плацебо). Додано, щоб ти перевірив це на своєму залізі бенчмарком, а не вірив на слово. Оборотно, потрібне перезавантаження.",
                         "Raises the interrupt priority of the real-time clock (IRQ8). A classic 'low-latency' tweak that on modern Windows often does NOTHING (placebo). Included so you can verify that on your own hardware with the benchmark instead of taking it on faith. Reversible, needs a reboot."),
                new L10n("?латентность (скорее плацебо)", "?латентність (скоріше плацебо)", "?latency (likely placebo)"),
                RegistryHive.LocalMachine, PriorityControl,
                new[] { new RegEntry("IRQ8Priority", RegistryValueKind.DWord, 1) },
                requiresRestart: true)),

            // Отключить «потоковые» DPC: обычные DPC обрабатываются без переноса в поток. На части
            // систем чуть ровнее латентность, на части — без разницы (спорно). Многоядерный CPU.
            new ExperimentalTweak(new RegistryTweak(
                "thread-dpc-off", TweakCategory.CpuPower, RiskLevel.Medium,
                new L10n("Отключить потоковые DPC", "Вимкнути потокові DPC", "Disable threaded DPCs"),
                new L10n("Windows перестаёт выносить отложенные вызовы (DPC) в отдельный поток. Теоретически ниже задержка обработки прерываний — ровнее фреймтайм. Эффект СПОРНЫЙ (у многих нет разницы). Обратимо, нужна перезагрузка. Замерь до/после.",
                         "Windows перестає виносити відкладені виклики (DPC) в окремий потік. Теоретично нижча затримка обробки переривань — рівніший фреймтайм. Ефект СУПЕРЕЧЛИВИЙ. Оборотно, потрібне перезавантаження. Зміряй до/після.",
                         "Windows stops offloading deferred procedure calls (DPCs) to a thread. In theory lower interrupt-handling latency — smoother frametime. Effect is DISPUTED (many see no difference). Reversible, needs a reboot. Measure before/after."),
                new L10n("?ровность (спорно)", "?рівність (спірно)", "?smoothness (disputed)"),
                RegistryHive.LocalMachine, KernelKey,
                new[] { new RegEntry("ThreadDpcEnable", RegistryValueKind.DWord, 0) },
                requiresRestart: true,
                supported: ctx => ctx.Hardware.CpuThreads >= 4)),

            // Увеличить системный кэш (LargeSystemCache). Может помочь на много-RAM (агрессивнее
            // кэширует), но МОЖЕТ и навредить (отдаёт память под файловый кэш в ущерб игре). Только 16 ГБ+.
            new ExperimentalTweak(new RegistryTweak(
                "large-system-cache", TweakCategory.Memory, RiskLevel.Medium,
                new L10n("Увеличить системный кэш", "Збільшити системний кеш", "Large system cache"),
                new L10n("Windows отдаёт больше памяти под системный кэш. На машинах с запасом RAM иногда ровнее подгрузка, но МОЖЕТ и навредить (кэш теснит игру) — строго под замер. Только при 16 ГБ+ RAM. Обратимо, нужна перезагрузка.",
                         "Windows віддає більше пам'яті під системний кеш. На машинах із запасом RAM іноді рівніше підвантаження, але МОЖЕ і нашкодити — строго під замір. Лише при 16 ГБ+ RAM. Оборотно, потрібне перезавантаження.",
                         "Windows gives more memory to the system cache. On RAM-rich machines loading can be smoother, but it MAY hurt (cache crowds the game) — measure carefully. 16 GB+ RAM only. Reversible, needs a reboot."),
                new L10n("?подгрузка (риск навредить)", "?підвантаження (ризик нашкодити)", "?loading (may hurt)"),
                RegistryHive.LocalMachine, MemoryManagement,
                new[] { new RegEntry("LargeSystemCache", RegistryValueKind.DWord, 1) },
                requiresRestart: true,
                supported: ctx => ctx.Hardware.RamGb >= 16)),

            // Меньший буфер очереди мыши. Теория: свежее ввод, ниже задержка. На практике ЧАСТО
            // плацебо. Значение 50 безопасно (слишком низкое ломает клики). Классика — под замер.
            new ExperimentalTweak(new RegistryTweak(
                "mouse-queue-size", TweakCategory.Frametime, RiskLevel.Medium,
                new L10n("Буфер очереди мыши", "Буфер черги миші", "Mouse queue size"),
                new L10n("Уменьшает буфер очереди данных мыши (100→50) — теоретически «свежее» ввод и ниже задержка. На практике ЧАСТО плацебо, но кто-то ощущает разницу. Значение 50 безопасно. Обратимо, нужна перезагрузка. Проверь на своём железе.",
                         "Зменшує буфер черги даних миші (100→50) — теоретично «свіжіший» ввід і нижча затримка. На практиці ЧАСТО плацебо. Значення 50 безпечне. Оборотно, потрібне перезавантаження.",
                         "Shrinks the mouse data queue buffer (100→50) — in theory 'fresher' input and lower latency. In practice OFTEN placebo, though some feel it. 50 is safe. Reversible, needs a reboot. Verify on your own hardware."),
                new L10n("?задержка ввода (часто плацебо)", "?затримка вводу (часто плацебо)", "?input lag (often placebo)"),
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters",
                new[] { new RegEntry("MouseDataQueueSize", RegistryValueKind.DWord, 50) },
                requiresRestart: true)),

            // Не обновлять «время последнего доступа» у файлов — меньше фоновых записей на диск.
            // Безопасно и обратимо; на современной Windows часто уже включено самой ОС.
            new ExperimentalTweak(new RegistryTweak(
                "ntfs-no-lastaccess", TweakCategory.Memory, RiskLevel.Safe,
                new L10n("NTFS: не писать время доступа", "NTFS: не писати час доступу", "NTFS: no last-access writes"),
                new L10n("NTFS перестаёт обновлять отметку «последнего доступа» при чтении файлов — меньше лишних фоновых записей на диск. Безопасно и обратимо. На новых Windows часто уже так по умолчанию (тогда эффекта нет). Замерь, если ловишь дисковые микрофризы.",
                         "NTFS перестає оновлювати позначку «останнього доступу» — менше зайвих фонових записів на диск. Безпечно й оборотно. На нових Windows часто вже так (тоді ефекту немає).",
                         "NTFS stops updating the 'last access' timestamp on reads — fewer needless background disk writes. Safe and reversible. On recent Windows this is often already the default (then no effect). Measure if you get disk-related micro-stutter."),
                new L10n("?меньше записей на диск", "?менше записів на диск", "?fewer disk writes"),
                RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\FileSystem",
                new[] { new RegEntry("NtfsDisableLastAccessUpdate", RegistryValueKind.DWord, 1) },
                // На новой Windows значение живёт как System-Managed (0x8000000X), и точная сверка
                // ==1 ложно гасила тумблер. Считаем применённым, если last-access ВЫКЛючен в ЛЮБОМ
                // режиме: младший бит = 1 (0x1 «User Managed, Disabled» или 0x80000001 «System Managed,
                // Disabled»). Так твик больше не «слетает», когда ОС нормализует значение.
                isApplied: _ =>
                {
                    using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                        .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\FileSystem");
                    return k?.GetValue("NtfsDisableLastAccessUpdate") is int v && (v & 1) == 1;
                })),

            // Активное удержание точного таймера 0.5 мс (пока работает приложение).
            new TimerResHoldTweak(),

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
        // «Скоро»-заглушки (IComingSoon) и эксперименты (IExperimental) не входят ни в один профиль.
        var byRisk = All().Where(t => t is not IComingSoon and not IExperimental).OrderBy(t => (int)t.Risk);
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
