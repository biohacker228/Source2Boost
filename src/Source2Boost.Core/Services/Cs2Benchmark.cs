using System.Diagnostics;

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

    /// <summary>Запуск CS2 через Steam-протокол (steam://run/730) с необязательными аргументами.</summary>
    private static bool LaunchViaSteam(string? launchArgs)
    {
        try
        {
            // steam://run/<appid>//<args> — Steam применяет args как параметры запуска игры.
            var uri = $"steam://run/{Cs2Paths.Cs2AppId}";
            if (!string.IsNullOrWhiteSpace(launchArgs))
                uri += "//" + Uri.EscapeDataString(launchArgs);
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
