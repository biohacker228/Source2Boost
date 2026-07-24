using System.Diagnostics;

namespace Source2Boost.Core;

/// <summary>
/// Чистка shader-кэшей CS2 и GPU — ТОЛЬКО ПО ЗАПРОСУ (кнопка), без таймера.
/// Раньше твик заводил ЕЖЕДНЕВНУЮ задачу планировщика, но это ВРЕДНО: полное стирание
/// шейдеров ведёт к более долгой загрузке и стуттеру рекомпиляции при следующем запуске.
/// Поэтому: (1) Apply больше НЕ планирует задачу; (2) сам твик убран из авто-каталога/профилей;
/// (3) сохранён публичный <see cref="CleanNow"/> для будущей ручной кнопки; (4) добавлен
/// идемпотентный снос старой задачи <see cref="RemoveLegacyDailyTask"/> для тех, кто её уже завёл.
/// Shader-кэши безопасно пересобираются, поэтому Risk = Safe.
/// </summary>
public sealed class ShaderCacheTweak : ITweak
{
    /// <summary>Имя старой (вредной) ежедневной задачи планировщика — сносится при апгрейде.</summary>
    public const string TaskName = "Source2Boost_ShaderCacheClean";

    public string Id => "shader-cache-clean";
    public TweakCategory Category => TweakCategory.Frametime;
    public RiskLevel Risk => RiskLevel.Safe;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Чистка shader-кэша (вручную)", "Чистка shader-кешу (вручну)", "Shader cache cleanup (manual)");
    public L10n Description { get; } = new(
        "Разовая чистка кэшей шейдеров CS2 и GPU по кнопке. БЕЗ ежедневного таймера: постоянное стирание шейдеров вредит — дольше грузится и даёт стуттер рекомпиляции. Применять только вручную после крупного обновления/сбоя шейдеров.",
        "Разова чистка кешів шейдерів CS2 та GPU за кнопкою. БЕЗ щоденного таймера: постійне стирання шейдерів шкодить — довше вантажиться та дає стуттер рекомпіляції. Застосовувати лише вручну після великого оновлення/збою шейдерів.",
        "One-off CS2/GPU shader cache cleanup on demand. NO daily timer: wiping shaders repeatedly hurts — slower loads and recompile stutter. Use manually only after a big update or a shader glitch.");
    public L10n Impact { get; } = new(
        "разовый сброс кэша", "разове скидання кешу", "one-off cache reset");

    public bool IsSupported(TweakContext ctx) => true;

    // Больше не персистентное состояние — это разовое действие, а не «включённый» твик.
    public bool IsApplied(TweakContext ctx) => false;

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            // Снести старую вредную ежедневную задачу, если пользователь её когда-то завёл.
            RemoveLegacyDailyTask(ctx.Trace);
            // Разовая чистка (никакого планирования).
            var result = CleanNow(ctx.Trace);
            ctx.Trace($"{Id}: {result}");
            return TweakResult.Ok(result);
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            // Откатывать нечего (кэш пересоберётся сам), но на всякий случай гарантируем,
            // что вредной ежедневной задачи не осталось.
            RemoveLegacyDailyTask(ctx.Trace);
            ctx.Trace($"{Id}: nothing to revert (caches rebuild automatically)");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Разовая чистка shader-кэшей «сейчас» для будущей ручной кнопки в UI.
    /// Возвращает короткую строку-итог. Ничего не планирует.
    /// </summary>
    public static string CleanNow(Action<string>? log = null)
    {
        var removed = Cleanup(log);
        return $"shader caches cleaned (~{removed} items)";
    }

    /// <summary>
    /// Идемпотентно удаляет ранее созданную вредную ежедневную задачу
    /// <see cref="TaskName"/>, если она существует (апгрейд-путь). Best-effort.
    /// </summary>
    public static void RemoveLegacyDailyTask(Action<string>? log = null)
    {
        try
        {
            if (!TaskExists()) return;
            Run("schtasks.exe", new[] { "/Delete", "/TN", TaskName, "/F" });
            log?.Invoke($"removed legacy harmful daily task {TaskName}");
        }
        catch (Exception ex) { log?.Invoke($"remove legacy task {TaskName}: {ex.Message}"); }
    }

    /// <summary>
    /// Целевые папки shader-кэша. Удаляется их СОДЕРЖИМОЕ, сами папки остаются.
    /// </summary>
    public static IReadOnlyList<string> CleanupDirs()
    {
        var list = new List<string>();
        var shader = Cs2Paths.Cs2ShaderCacheDir();
        if (shader is not null) list.Add(shader);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        list.Add(Path.Combine(local, "NVIDIA", "DXCache"));
        list.Add(Path.Combine(local, "NVIDIA", "GLCache"));
        list.Add(Path.Combine(local, "D3DSCache"));
        return list;
    }

    /// <summary>
    /// Немедленная чистка (переиспользуемый метод). Удаляет содержимое папок из
    /// <see cref="CleanupDirs"/>, игнорируя отсутствующие пути и заблокированные файлы.
    /// Возвращает примерное число удалённых элементов верхнего уровня.
    /// </summary>
    public static int Cleanup(Action<string>? log = null)
    {
        var removed = 0;
        foreach (var dir in CleanupDirs())
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    try { File.Delete(f); removed++; } catch { /* заблокирован — пропускаем */ }
                }
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try { Directory.Delete(sub, recursive: true); removed++; } catch { }
                }
                log?.Invoke($"cleaned {dir}");
            }
            catch (Exception ex) { log?.Invoke($"cleanup {dir}: {ex.Message}"); }
        }
        return removed;
    }

    private static bool TaskExists()
    {
        var outp = Run("schtasks.exe", new[] { "/Query", "/TN", TaskName });
        return outp.Contains(TaskName, StringComparison.OrdinalIgnoreCase);
    }

    private static string Run(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            return o;
        }
        catch { return ""; }
    }
}
