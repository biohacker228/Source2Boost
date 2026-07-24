using System.Diagnostics;

namespace Source2Boost.Core;

/// <summary>
/// Периодическая очистка standby-списка памяти (ISLC-подобно) — главный анти-стуттер на
/// ограниченной/разнокалиберной RAM. Standby-кэш переполняется и вызывает всплески фреймтайма;
/// периодический сброс убирает микро-стуттер. Заменяет прежний вредный shader-таймер.
///
/// Apply: создаёт задачу планировщика <see cref="TaskName"/>, которая РАЗ В СУТКИ запускает
/// сам exe с ключом <c>--clean-standby --force</c> (headless, без окна; чистит БЕЗУСЛОВНО).
/// Revert: удаляет задачу. IsApplied: задача существует.
/// Полностью обратимо, риск = Safe.
/// </summary>
public sealed class StandbyCleanTweak : ITweak
{
    /// <summary>Имя задачи планировщика (раз в сутки).</summary>
    public const string TaskName = "Source2Boost_StandbyClean";

    public string Id => "standby-clean";
    public TweakCategory Category => TweakCategory.Memory;
    public RiskLevel Risk => RiskLevel.Safe;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Очистка standby-памяти (антистуттер)", "Очищення standby-пам'яті (антистуттер)", "Standby memory cleaner (anti-stutter)");
    public L10n Description { get; } = new(
        "Раз в сутки в фоне очищает «зависший» кэш оперативной памяти — он со временем забивается и вызывает всплески кадров и микро-стуттер. Полезно даже при большом объёме RAM. Полностью обратимо (удаляет задачу).",
        "Раз на добу у фоні очищає «завислий» кеш оперативної пам'яті — він з часом забивається і спричиняє сплески кадрів та мікро-стуттер. Корисно навіть за великого обсягу RAM. Повністю оборотно (видаляє задачу).",
        "Once a day in the background it clears the 'stuck' RAM cache — it fills up over time and causes frame spikes and micro-stutter. Useful even with lots of RAM. Fully reversible (removes the task).");
    public L10n Impact { get; } = new(
        "-микрофризы памяти", "-мікрофризи пам'яті", "-memory micro-stutter");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx) => TaskExists();

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            // Апгрейд-путь: этот твик заменяет прежний вредный shader-таймер — сносим старую задачу.
            ShaderCacheTweak.RemoveLegacyDailyTask(ctx.Trace);

            var exe = ExePath();
            if (exe is null) return TweakResult.Fail("cannot resolve Source2Boost.exe path");

            // /TR: "<полный путь к exe>" --clean-standby --force  (кавычки вокруг пути обязательны).
            var tr = $"\"{exe}\" --clean-standby --force";
            var outp = Run("schtasks.exe", new[]
            {
                "/Create", "/TN", TaskName, "/TR", tr,
                "/SC", "DAILY", "/ST", "12:00", "/RL", "HIGHEST", "/F"
            });
            ctx.Trace($"{Id}: schtasks create -> {outp.Trim()}");

            return TaskExists()
                ? TweakResult.Ok()
                : TweakResult.Fail("scheduled task not created: " + outp.Trim());
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            Run("schtasks.exe", new[] { "/Delete", "/TN", TaskName, "/F" });
            ctx.Trace($"{Id}: scheduled task deleted");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>Полный путь к текущему exe (Source2Boost.exe).</summary>
    public static string? ExePath()
    {
        var p = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(p)) return p;
        try { return Process.GetCurrentProcess().MainModule?.FileName; }
        catch { return null; }
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
