using System.Diagnostics;

namespace Source2Boost.Core;

/// <summary>
/// Отключает сжатие памяти Windows (Memory Compression, часть MMAgent). Метод про-тюнеров:
/// Windows тратит CPU на сжатие/разжатие страниц RAM в фоне; на CPU-bound связке это лишняя
/// нагрузка. Отключение освобождает CPU-время (ценой чуть большего расхода RAM/подкачки).
///
/// Apply: сохраняет прежнее состояние <c>(Get-MMAgent).MemoryCompression</c> в ctx.Backup и
/// выполняет <c>Disable-MMAgent -MemoryCompression</c>. Revert: <c>Enable-MMAgent -MemoryCompression</c>
/// ТОЛЬКО если изначально было включено. Эффект — после перезагрузки. Risk = Medium.
/// </summary>
public sealed class MemoryCompressionTweak : ITweak
{
    public string Id => "memory-compression-off";
    public TweakCategory Category => TweakCategory.Memory;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => true;

    public L10n Title { get; } = new(
        "Отключить сжатие памяти", "Вимкнути стиснення пам'яті", "Disable memory compression");
    public L10n Description { get; } = new(
        "Windows перестаёт тратить процессор на сжатие страниц памяти в фоне. Когда упор в CPU, это освобождает процессорное время под игру (ценой чуть большего расхода RAM). Обратимо, эффект после перезагрузки.",
        "Windows перестає витрачати процесор на стиснення сторінок пам'яті у фоні. Коли впор у CPU, це вивільняє процесорний час під гру (ціною трохи більшої витрати RAM). Оборотно, ефект після перезавантаження.",
        "Windows stops spending CPU to compress memory pages in the background. When you're CPU-bound, this frees CPU time for the game (at the cost of slightly more RAM use). Reversible, takes effect after a reboot.");
    public L10n Impact { get; } = new(
        "-нагрузка CPU на сжатие", "-навантаження CPU на стиснення", "-CPU spent compressing RAM");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx) => QueryMemoryCompression() == false;

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            // Снимок исходного состояния фиксируем ТОЛЬКО ОДИН РАЗ (повторный Apply не затирает истину).
            if (ctx.Backup.LoadState(Id) is null)
            {
                var prior = QueryMemoryCompression();
                // "true"/"false"/"unknown" — при откате включаем обратно только если было true.
                ctx.Backup.SaveState(Id, prior is null ? "unknown" : (prior.Value ? "true" : "false"));
            }

            var outp = RunPs("Disable-MMAgent -MemoryCompression");
            ctx.Trace($"applied {Id}: Disable-MMAgent -MemoryCompression {outp.Trim()}");

            return IsApplied(ctx)
                ? TweakResult.Ok()
                : TweakResult.Ok("applied; effect after reboot"); // Get-MMAgent может ещё показывать True до ребута
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            var state = ctx.Backup.LoadState(Id);
            // Возвращаем сжатие ТОЛЬКО если оно изначально было включено.
            if (state is null || state.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                var outp = RunPs("Enable-MMAgent -MemoryCompression");
                ctx.Trace($"reverted {Id}: Enable-MMAgent -MemoryCompression {outp.Trim()}");
            }
            else
            {
                ctx.Trace($"reverted {Id}: was originally disabled ({state}), leaving off");
            }
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>Текущее состояние сжатия памяти: true/false, или null если распарсить не удалось.</summary>
    private static bool? QueryMemoryCompression()
    {
        var outp = RunPs("(Get-MMAgent).MemoryCompression").Trim();
        if (outp.StartsWith("True", StringComparison.OrdinalIgnoreCase)) return true;
        if (outp.StartsWith("False", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static string RunPs(string command)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            return o;
        }
        catch { return ""; }
    }
}
