namespace Source2Boost.Core;

/// <summary>
/// Лечит болячку Source 2 на «сложных» CPU: движок неправильно раскладывает главный поток —
/// у Intel-гибридов уводит его на медленные E-ядра, у много-CCD Ryzen не держится за быстрый/
/// кэш-CCD → просадки FPS и стуттер даже на мощном железе. Твик сажает cs2.exe на правильные
/// ядра (P-ядра / первый CCD). CS2 может сбрасывать аффинити, поэтому реально применяется и
/// ПЕРЕПРИМЕНЯЕТСЯ при каждом запуске игры сторожем (App), а здесь хранится флаг-намерение.
///
/// Виден ТОЛЬКО на гибридных/много-CCD CPU (IsSupported). На обычном CPU скрыт — там проблемы
/// планирования нет. Полностью обратим (Revert возвращает все ядра). Risk = Medium.
/// </summary>
public sealed class Cs2AffinityTweak : ITweak
{
    public const string StateKey = "cs2-affinity";

    public string Id => "cs2-affinity";
    public TweakCategory Category => TweakCategory.CpuPower;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "CS2 на правильные ядра", "CS2 на правильні ядра", "Pin CS2 to the right cores");

    public L10n Description { get; } = new(
        "Сажает CS2 на быстрые ядра (P-ядра у Intel-гибрида / первый CCD у Ryzen). Движок Source 2 сам раскладывает главный поток неудачно — уводит на E-ядра или медленный CCD, отсюда просадки и стуттер даже на сильном CPU. Применяется при каждом запуске CS2 (игра иногда сбрасывает — переставляем).",
        "Садить CS2 на швидкі ядра (P-ядра у Intel-гібрида / перший CCD у Ryzen). Рушій Source 2 сам розкладає головний потік невдало — тягне на E-ядра або повільний CCD, звідси просадки та стуттер навіть на сильному CPU. Застосовується при кожному запуску CS2.",
        "Pins CS2 to the fast cores (P-cores on Intel hybrids / first CCD on Ryzen). Source 2 schedules its main thread poorly — onto E-cores or the slow CCD — causing drops and stutter even on strong CPUs. Reapplied every time CS2 launches (the game sometimes resets it).");

    public L10n Impact { get; } = new(
        "+ровность (−стуттер)", "+рівність (−стуттер)", "+smoothness (−stutter)");

    /// <summary>Показываем только там, где топология реально мешает CS2 (гибрид / много-CCD).</summary>
    public bool IsSupported(TweakContext ctx)
        => CpuTopology.Detect(ctx.Hardware).Kind != CpuTopologyKind.Simple;

    public bool IsApplied(TweakContext ctx) => ctx.Backup.LoadState(StateKey) == "1";

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            ctx.Backup.SaveState(StateKey, "1");
            // Если CS2 уже запущен — применим сразу; иначе подхватит сторож при следующем старте.
            var mask = CpuTopology.Detect(ctx.Hardware).RecommendedMask;
            int n = Cs2Affinity.Apply(mask);
            ctx.Trace($"{Id}: enabled, applied to {n} running cs2 proc(s), mask=0x{mask:X}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            ctx.Backup.SaveState(StateKey, "0");
            Cs2Affinity.Reset();   // вернуть все ядра запущенному CS2 (если есть)
            ctx.Trace($"{Id}: disabled, affinity reset to all cores");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }
}
