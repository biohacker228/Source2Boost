namespace Source2Boost.Core;

/// <summary>
/// Модуль CS2: рекомендованные параметры запуска и генерация/установка autoexec.cfg.
/// Только валидные cvar Source 2 (без CSGO-мусора: -d3d9ex, mat_queue_mode, cl_interp/cmdrate и т.п.).
/// </summary>
public static class Cs2Config
{
    /// <summary>
    /// Рекомендованные параметры запуска Steam для CS2. Только проверенные аргументы (2026):
    /// -mainthreadpriority 2 подтверждён как легитимный (ровнее фреймтайм/1% low).
    /// НЕ включены: fps_max (в autoexec капом у стабильного потолка), +thread_pool_option
    /// (несуществующий cvar), -threads (может ронять Source 2).
    /// УБРАН -softparticlesdefaultoff — это legacy CS:GO, в Source 2 не работает (2026).
    /// УБРАН -high — на слабом CPU конкурирует с аудио-потоком и может ДАВАТЬ микрофризы
    /// (источники 2026 + s1mple его не использует); приоритет CS2 задаётся отдельным твиком
    /// cs2-high-priority по выбору пользователя, дублировать в параметрах запуска не нужно.
    /// -freq подставляется динамически (см. <see cref="LaunchOptionsFor"/>).
    /// </summary>
    public const string LaunchOptions =
        "-novid -console -nojoy -fullscreen -mainthreadpriority 2 +exec autoexec";

    /// <summary>
    /// Параметры запуска под конкретное железо. Добавляет <c>-freq &lt;герцовка&gt;</c> для
    /// high-refresh монитора (≥100 Гц) — форсит высокую частоту как у про (s1mple: -freq 360),
    /// и это безопасно: если читаем ≥100, значит это точно быстрый монитор, а не ошибочные 60.
    /// Для 60-герцовых/неизвестных -freq НЕ ставим — CS2 в fullscreen сам берёт нужную.
    /// </summary>
    public static string LaunchOptionsFor(HardwareInfo? hw = null, bool overlay = false)
    {
        var parts = new List<string> { "-novid", "-console", "-nojoy", "-fullscreen", "-mainthreadpriority 2" };
        int hz = hw?.MonitorHz ?? 0;
        if (hz >= 100) parts.Add($"-freq {hz}");
        if (overlay) parts.Add("-allow_third_party_software");
        parts.Add("+exec autoexec");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Рекомендуемый кап fps_max. ЛОГИКА: опора — СПОСОБНОСТЬ железа (реальный замер важнее
    /// прогноза), а НЕ герцовка монитора.
    ///
    /// Почему герцовка не участвует: верт. синхронизацию мы выключаем, поэтому кадры ВЫШЕ
    /// герцовки в CS2 не пропадают зря — на момент развёртки берётся более свежий кадр, а это
    /// меньше задержка (потому про-игроки и держат FPS сильно выше 144/240). Ограничивать кап
    /// герцовкой = осознанно добавлять себе задержку. Обратный случай тоже бесполезен: если
    /// железо тянет 100, а монитор 180 — кап в 180 не делает вообще ничего.
    ///
    /// Ровный фреймтайм даёт кап чуть НИЖЕ стабильного потолка (98%): в лёгких сценах кадры
    /// «подрезаются» до уровня, который система держит и в тяжёлых — меньше скачков.
    /// Если способность неизвестна — 0 (без лимита): это безопасно для задержки.
    /// </summary>
    public static int RecommendFpsCap(double measuredAvg = 0, double predictedAvg = 0, double measuredLow1 = 0)
    {
        // Лучший ориентир РОВНОГО пейсинга — измеренный 1% low: это FPS, который система держит
        // даже в тяжёлых сценах. Кап чуть ниже него (98%) = почти нулевые скачки фреймтайма.
        // Компромисс: средний FPS «подрезается» ради ровности (это осознанный выбор про-плавность).
        if (measuredLow1 > 0) return Math.Max(60, (int)Math.Floor(measuredLow1 * 0.98));
        // Нет 1% low — опираемся на среднюю способность (замер важнее прогноза).
        double basis = measuredAvg > 0 ? measuredAvg : predictedAvg;
        if (basis <= 0) return 0;                        // ничего не знаем — без лимита
        return Math.Max(60, (int)Math.Floor(basis * 0.98));
    }

    /// <summary>
    /// Содержимое autoexec.cfg. <paramref name="fpsCap"/> — значение fps_max (0 = без лимита).
    /// Если не передан, берётся прогноз FPS по железу (герцовка на кап не влияет — см.
    /// <see cref="RecommendFpsCap"/>).
    /// </summary>
    public static string AutoexecContent(HardwareInfo? hw = null, int? fpsCap = null)
    {
        int cap = fpsCap ?? RecommendFpsCap(predictedAvg: hw is null ? 0 : FpsEstimator.HeuristicBaseline(hw));
        var lines = new List<string>
        {
            "// ==============================================",
            "//  Source2Boost - autoexec.cfg  (CS2 / Source 2)",
            "//  Сгенерировано автоматически. Только валидные cvar CS2.",
            "//  Применяется параметром запуска:  +exec autoexec",
            "// ==============================================",
            "",
            $"fps_max {cap}".PadRight(26) + "// кап у стабильного потолка = ровный фреймтайм (0 = без лимита)",
            "fps_max_ui 120".PadRight(26) + "// кап FPS в меню - экономит нагрузку вне матча",
            "engine_no_focus_sleep 0".PadRight(26) + "// не троттлить движок при потере фокуса",
            "snd_mute_losefocus 0".PadRight(26) + "// звук не глушится при alt-tab (по желанию 1)",
            "rate 786432".PadRight(26) + "// максимальный сетевой rate CS2",
            "",
            "// Логика fps_max: ставь чуть НИЖЕ среднего FPS из своего замера - меньше скачков.",
            "// Кап по герцовке НЕ нужен: vsync выключен, кадры выше герцовки снижают задержку.",
            "",
        };
        lines.Add("echo \"[Source2Boost] autoexec loaded\"");
        lines.Add("host_writeconfig");
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Путь к целевому autoexec.cfg, или null если папка cfg CS2 не найдена.</summary>
    public static string? AutoexecPath()
    {
        var cfg = Cs2Paths.Cs2CfgDir();
        return cfg is null ? null : Path.Combine(cfg, "autoexec.cfg");
    }

    /// <summary>true, если autoexec.cfg уже установлен нашей программой (по маркеру).</summary>
    public static bool IsAutoexecInstalled()
    {
        var p = AutoexecPath();
        try { return p is not null && File.Exists(p) && File.ReadAllText(p).Contains("Source2Boost"); }
        catch { return false; }
    }

    /// <summary>
    /// Записать autoexec.cfg в папку cfg CS2. Существующий чужой файл бэкапится рядом (.bak).
    /// Возвращает путь записанного файла или бросает с понятным сообщением.
    /// </summary>
    public static string InstallAutoexec(HardwareInfo? hw = null, int? fpsCap = null)
    {
        var cfgDir = Cs2Paths.Cs2CfgDir()
            ?? throw new InvalidOperationException("Папка cfg CS2 не найдена. Установлен ли CS2 и запускался ли Steam?");
        Directory.CreateDirectory(cfgDir);
        var path = Path.Combine(cfgDir, "autoexec.cfg");

        // бэкап чужого существующего autoexec (не нашего)
        if (File.Exists(path))
        {
            try
            {
                var existing = File.ReadAllText(path);
                if (!existing.Contains("Source2Boost"))
                {
                    var bak = Path.Combine(cfgDir, $"autoexec.bak-{DateTime.Now:yyyyMMdd_HHmmss}.cfg");
                    File.Copy(path, bak, overwrite: false);
                }
            }
            catch { /* бэкап best-effort */ }
        }

        File.WriteAllText(path, AutoexecContent(hw, fpsCap));
        return path;
    }
}
