using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Авто-замер CS2 через ВОСПРОИЗВЕДЕНИЕ ЭТАЛОННОЙ ДЕМКИ (playdemo). Почему демка, а не боты:
/// боты недетерминированы и сами режут FPS сильнее игроков — по такому прогону нельзя честно
/// сравнивать «до/после». Демка же проигрывает одну и ту же сцену (те же смоки, движение)
/// каждый раз одинаково — это золотой стандарт A/B-бенчмарка.
///
/// Эталонную демку <see cref="ReferenceDemoName"/> нужно один раз записать в игре и положить в
/// ...\game\csgo\replays\. Пока её нет — авто-замер по демке недоступен, и UI предлагает записать
/// её (разово) либо мерить обычным ручным замером. Плюмбинг запуска и захвата готов заранее.
/// </summary>
public static class Cs2Benchmark
{
    /// <summary>Имя эталонной демки (без расширения), лежащей в ...\csgo\replays\.</summary>
    public const string ReferenceDemoName = "s2b_bench";

    /// <summary>Полный путь к эталонной демке в папке replays, если она есть; иначе null.</summary>
    public static string? ReferenceDemoPath()
    {
        var dir = Cs2Paths.Cs2ReplaysDir();
        if (dir is null) return null;
        var p = Path.Combine(dir, ReferenceDemoName + ".dem");
        return File.Exists(p) ? p : null;
    }

    /// <summary>Есть ли эталонная демка (сначала пытаемся установить вложённую в комплект).</summary>
    public static bool HasReferenceDemo()
    {
        EnsureReferenceDemoInstalled();
        return ReferenceDemoPath() is not null;
    }

    /// <summary>Путь к демке, ВЛОЖЁННОЙ в комплект (рядом с exe; в dev — assets\ репозитория).
    /// Установщик кладёт s2b_bench.dem рядом с приложением; отсюда она копируется в replays.</summary>
    public static string? BundledDemoPath()
    {
        var file = ReferenceDemoName + ".dem";
        var dirs = new List<string> { AppContext.BaseDirectory };
        try
        {
            // dev-фолбэк: ...\src\App\bin\Debug\net8.0-windows -> подняться к корню репозитория\assets
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && d is not null; i++, d = d.Parent)
            {
                var assets = Path.Combine(d.FullName, "assets");
                if (Directory.Exists(assets)) { dirs.Add(assets); break; }
            }
        }
        catch { }
        foreach (var dir in dirs)
        {
            var p = Path.Combine(dir, file);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>Идемпотентно копирует вложённую демку в папку replays CS2, если её там ещё нет.
    /// Благодаря этому конечный пользователь НИКОГДА не открывает консоль — демка уже на месте.</summary>
    public static void EnsureReferenceDemoInstalled()
    {
        try
        {
            var replays = Cs2Paths.Cs2ReplaysDir();
            var bundled = BundledDemoPath();
            if (replays is null || bundled is null) return;
            var target = Path.Combine(replays, ReferenceDemoName + ".dem");
            if (File.Exists(target)) return;             // уже установлена
            Directory.CreateDirectory(replays);
            File.Copy(bundled, target);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Запустить CS2 сразу на воспроизведение эталонной демки (детерминированная сцена).
    /// Аргумент запуска <c>+playdemo replays/s2b_bench</c> исполняется при старте без консоли.</summary>
    public static bool LaunchDemoPlayback()
        => LaunchViaSteam($"+playdemo replays/{ReferenceDemoName}");

    /// <summary>Просто запустить/сфокусировать CS2 (когда эталонной демки ещё нет).</summary>
    public static bool LaunchGame() => LaunchViaSteam(null);

    /// <summary>Открыть папку replays в проводнике (чтобы положить/проверить демку).</summary>
    public static bool OpenReplaysFolder()
    {
        var dir = Cs2Paths.Cs2ReplaysDir();
        if (dir is null) return false;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    // ---------- Воркшоп-бенч-карта (детерминированный сценарий с авто-прогоном) ----------

    /// <summary>ID карты-бенчмарка по умолчанию: «CS2 FPS BENCHMARK ANCIENT» (живая, формат
    /// вывода [VProf] FPS: Avg=..., P1=...). ID НАСТРАИВАЕМЫЙ — если карту удалят, юзер меняет
    /// на другую, не завися от одной конкретной.</summary>
    public const string DefaultWorkshopMapId = "3472126051";

    /// <summary>Запустить CS2 для бенчмарка. <c>-condebug</c> заставляет игру писать консоль в
    /// console.log, откуда мы ловим ИТОГ ЛЮБОЙ карты-бенчмарка (строка <c>[VProf] FPS: Avg=..., P1=...</c>
    /// — общий формат популярных бенч-карт). <c>+fps_max 0</c> снимает лимит FPS на сессию (иначе замер
    /// упрётся в кап); autoexec не трогаем — обычный кап вернётся при нормальном запуске.
    ///
    /// Карту НЕ грузим сами: CS2 игнорирует host_workshop_map как стартовую команду, а подать команду
    /// извне в CS2 нельзя (netcon удалён, VConsole-подключение = риск VAC). Поэтому карту пользователь
    /// выбирает сам в игре (Играть → Мастерская) — зато мы не привязаны к конкретной карте.
    ///
    /// ЗАПУСК: напрямую cs2.exe (steam://run в CS2 не передаёт «+команды»), рабочая папка = ...\bin\win64.
    /// Фолбэк на steam:// если exe не найден.</summary>
    public static bool LaunchForBenchmark()
    {
        const string args = "-condebug +fps_max 0";
        var exe = Cs2Paths.Cs2ExePath();
        if (exe is not null)
        {
            try
            {
                var dir = Path.GetDirectoryName(exe)!;
                Logger.Info($"benchmark: launch cs2.exe direct, exe={exe}, args={args}");
                Process.Start(new ProcessStartInfo(exe)
                {
                    Arguments = args,
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                });
                return true;
            }
            catch (Exception ex) { Logger.Info($"benchmark: direct launch failed ({ex.Message}), fallback to steam://"); }
        }
        Logger.Info($"benchmark: launch via steam://, args={args}");
        return LaunchViaSteam(args);
    }

    /// <summary>Открыть страницу карты в мастерской Steam (чтобы подписаться — разово).</summary>
    public static bool OpenWorkshopPage(string mapId)
    {
        if (!IsValidId(mapId)) return false;
        try
        {
            Process.Start(new ProcessStartInfo($"steam://url/CommunityFilePage/{mapId}") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Скачана ли (подписана) карта в мастерской — можно ли её грузить host_workshop_map.</summary>
    public static bool IsMapDownloaded(string mapId)
        => IsValidId(mapId) && Cs2Paths.Cs2WorkshopMapDir(mapId) is not null;

    /// <summary>Путь к console.log (пишется при запуске с -condebug), или null.</summary>
    public static string? ConsoleLogPath()
    {
        var csgo = Cs2Paths.Cs2CsgoDir();
        return csgo is null ? null : Path.Combine(csgo, "console.log");
    }

    /// <summary>Разобрать ПОСЛЕДНЮЮ строку итога карты «[VProf] FPS: Avg=517.3, P1=183.5» из
    /// console.log. Возвращает (средний FPS, 1% low) или null. Файл открыт игрой — читаем с share.</summary>
    public static (double avg, double p1)? ParseVProfResult()
    {
        var path = ConsoleLogPath();
        if (path is null || !File.Exists(path)) return null;
        try
        {
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
                text = sr.ReadToEnd();
            var m = Regex.Matches(text, @"\[VProf\]\s*FPS:\s*Avg=([\d.]+),\s*P1=([\d.]+)", RegexOptions.IgnoreCase);
            if (m.Count == 0) return null;
            var last = m[^1];
            return (double.Parse(last.Groups[1].Value, CultureInfo.InvariantCulture),
                    double.Parse(last.Groups[2].Value, CultureInfo.InvariantCulture));
        }
        catch { return null; }
    }

    /// <summary>Очистить console.log перед прогоном (чтобы не поймать старый VProf-итог). Best-effort.</summary>
    public static void ClearConsoleLog()
    {
        try { var p = ConsoleLogPath(); if (p is not null && File.Exists(p)) File.WriteAllText(p, ""); }
        catch { }
    }

    /// <summary>ID мастерской — только цифры (защита от мусора/инъекций в аргумент запуска).</summary>
    public static bool IsValidId(string? id) => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, @"^\d{6,15}$");

    /// <summary>Запуск CS2 через Steam-протокол (steam://run/730) с необязательными аргументами.
    /// ВАЖНО про кодирование: НЕ используем EscapeDataString — он кодирует '+' в '%2B', а Steam
    /// это не раскодирует, и консольные команды ('+host_workshop_map', '+playdemo') не срабатывают
    /// (игра просто открывается в меню). Кодируем ТОЛЬКО пробелы в %20 — так же, как это делают
    /// сторонние сайты-лаунчеры; '+' и '-' оставляем как есть.</summary>
    private static bool LaunchViaSteam(string? launchArgs)
    {
        try
        {
            var uri = $"steam://run/{Cs2Paths.Cs2AppId}";
            if (!string.IsNullOrWhiteSpace(launchArgs))
                uri += "//" + launchArgs.Trim().Replace(" ", "%20");
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
