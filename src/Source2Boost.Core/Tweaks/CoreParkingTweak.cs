using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Source2Boost.Core;

/// <summary>
/// Полностью отключает парковку ядер и держит частоту CPU: на АКТИВНОЙ схеме питания
/// SUB_PROCESSOR CPMINCORES=100 (нет парковки), PROCTHROTTLEMIN=100, PROCTHROTTLEMAX=100.
/// Скрытые настройки предварительно раскрываются через powercfg -attributes.
/// Точный откат: исходные AC-значения парсятся из powercfg /query и восстанавливаются.
/// </summary>
public sealed class CoreParkingTweak : ITweak
{
    // GUID подгруппы процессора и трёх настроек внутри неё.
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private static readonly (string Alias, string Guid)[] Settings =
    {
        ("CPMINCORES",      "0cc5b647-c1df-4637-891a-dec35c318583"), // минимум ядер (нет парковки при 100)
        ("PROCTHROTTLEMIN", "893dee8e-2bef-41e0-89c6-b55d0929964c"), // мин. состояние процессора
        ("PROCTHROTTLEMAX", "bc5038f7-23e0-4960-96da-33abaf5935ec"), // макс. состояние процессора
    };

    public string Id => "core-parking-off";
    public TweakCategory Category => TweakCategory.CpuPower;
    public RiskLevel Risk => RiskLevel.Medium;
    public bool RequiresRestart => false;

    public L10n Title { get; } = new(
        "Отключить парковку ядер", "Вимкнути паркування ядер", "Disable core parking");
    public L10n Description { get; } = new(
        "На активном плане питания: минимум ядер=100% (парковки нет), мин./макс. состояние CPU=100% — частота не гуляет, ровнее фреймтайм. Обратимо.",
        "На активному плані живлення: мінімум ядер=100% (паркування немає), мін./макс. стан CPU=100% — частота не гуляє, рівніший фреймтайм. Оборотно.",
        "On the active power plan: min cores=100% (no parking), min/max CPU state=100% — clocks stop wandering, steadier frametime. Reversible.");
    public L10n Impact { get; } = new(
        "+ровный фреймтайм", "+рівний фреймтайм", "+steady frametime");

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx)
    {
        foreach (var (alias, guid) in Settings)
        {
            var v = QueryAcIndex(guid);
            if (v is null || v.Value != 100) return false;
        }
        return true;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            // Снимок оригиналов фиксируем ТОЛЬКО ОДИН РАЗ — повторный Apply не затирает истинный исходник.
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, int>>(prior) ?? new())
                : new Dictionary<string, int>();

            foreach (var (alias, guid) in Settings)
            {
                if (!originals.ContainsKey(alias))
                {
                    var cur = QueryAcIndex(guid);
                    if (cur is not null) originals[alias] = cur.Value;
                }
                // раскрыть настройку, если она скрыта в интерфейсе (best-effort)
                RunPowercfg($"-attributes {SubProcessor} {guid} -ATTRIB_HIDE");
                RunPowercfg($"-setacvalueindex scheme_current {SubProcessor} {guid} 100");
            }
            RunPowercfg("-setactive scheme_current");

            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            ctx.Trace($"applied {Id}: parking off, proc state 100/100");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    public TweakResult Revert(TweakContext ctx)
    {
        try
        {
            var json = ctx.Backup.LoadState(Id);
            var originals = json is null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, int>>(json);

            foreach (var (alias, guid) in Settings)
            {
                // если исходника нет — безопасный дефолт Windows: 5% для CPMINCORES/PROCTHROTTLEMIN, 100% для MAX
                var val = originals is not null && originals.TryGetValue(alias, out var o)
                    ? o
                    : (alias == "PROCTHROTTLEMAX" ? 100 : 5);
                RunPowercfg($"-setacvalueindex scheme_current {SubProcessor} {guid} {val}");
            }
            RunPowercfg("-setactive scheme_current");
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Текущий AC-индекс настройки активной схемы, или null если распарсить не удалось.
    /// ВАЖНО: вывод powercfg локализован (рус. «Текущий индекс настройки питания от сети: 0x...»),
    /// поэтому НЕ ищем англоязычную подпись. Вместо этого берём предпоследнее шестнадцатеричное
    /// значение 0x — powercfg всегда печатает пары «текущий AC», затем «текущий DC» последними,
    /// после min/max/increment. Это не зависит от языка Windows.
    /// </summary>
    private static int? QueryAcIndex(string settingGuid)
    {
        var outp = RunPowercfg($"/query scheme_current {SubProcessor} {settingGuid}");
        var ms = Regex.Matches(outp, @"0x([0-9a-fA-F]+)");
        // Нужны как минимум два 0x (AC и DC); AC — предпоследний.
        if (ms.Count < 2) return null;
        var acHex = ms[ms.Count - 2].Groups[1].Value;
        return int.TryParse(acHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? v : (int?)null;
    }

    private static string RunPowercfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return o;
        }
        catch { return ""; }
    }
}
