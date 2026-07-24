using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// Отключение алгоритма Нейгла на всех сетевых интерфейсах:
/// TcpAckFrequency=1 + TCPNoDelay=1. Мелкие TCP-пакеты уходят сразу,
/// без задержки склейки. Точный откат: сохраняем исходники по каждому интерфейсу.
/// </summary>
public sealed class NagleTweak : ITweak
{
    private const string InterfacesKey =
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private static readonly string[] Names = { "TcpAckFrequency", "TCPNoDelay" };

    public string Id => "nagle-off";
    public TweakCategory Category => TweakCategory.Network;
    public RiskLevel Risk => RiskLevel.High;
    public bool RequiresRestart => true;

    public L10n Title { get; } = new(
        "Отключить алгоритм Нейгла", "Вимкнути алгоритм Нейгла", "Disable Nagle's algorithm");
    public L10n Description { get; } = new(
        "Отключает алгоритм Нейгла на всех сетевых интерфейсах — мелкие пакеты игры уходят сразу, без задержки на склейку. Ниже задержка в сети.",
        "Вимикає алгоритм Нейгла на всіх мережевих інтерфейсах — дрібні пакети гри йдуть одразу, без затримки на склеювання. Нижча затримка в мережі.",
        "Turns off Nagle's algorithm on every network interface — the game's small packets leave immediately, no coalescing delay. Lower network latency.");
    public L10n Impact { get; } = new(
        "-сетевая латентность", "-мережева латентність", "-network latency");

    public bool IsSupported(TweakContext ctx) => true;

    private static RegistryKey Root =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    public bool IsApplied(TweakContext ctx)
    {
        using var root = Root;
        using var ifaces = root.OpenSubKey(InterfacesKey);
        if (ifaces is null) return false;

        // Проверяем ТОЛЬКО интерфейсы, на которые реально применяли (из состояния Apply).
        // Раньше требовали значение на КАЖДОМ интерфейсе — но у активного юзера постоянно
        // появляются новые виртуальные адаптеры (VPN/WSL/Docker/Hyper-V), и один такой без
        // нашего значения ложно «гасил» твик (тумблер откатывался сам). Новые адаптеры теперь
        // игнорируются; исчезнувшие — тоже (пропускаем). Без состояния — старое поведение.
        IEnumerable<string> targets;
        var json = ctx.Backup.LoadState(Id);
        if (json is not null)
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, OriginalValue>>>(json);
            var keys = saved?.Keys.ToList();
            if (keys is null || keys.Count == 0) return false;   // применяли, но записей нет → не применено
            targets = keys;
        }
        else
        {
            targets = ifaces.GetSubKeyNames();
        }

        bool anyChecked = false;
        foreach (var g in targets)
        {
            using var k = ifaces.OpenSubKey(g);
            if (k is null) continue;   // интерфейс исчез — не считаем это провалом
            anyChecked = true;
            foreach (var n in Names)
                if (k.GetValue(n) is not int v || v != 1) return false;
        }
        return anyChecked;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            ctx.Backup.BackupRegistryKey($"HKLM\\{InterfacesKey}");
            var originals = new Dictionary<string, Dictionary<string, OriginalValue>>();
            using (var root = Root)
            using (var ifaces = root.OpenSubKey(InterfacesKey, writable: true))
            {
                if (ifaces is null) return TweakResult.Fail("no TCP interfaces key");
                foreach (var g in ifaces.GetSubKeyNames())
                {
                    using var k = ifaces.OpenSubKey(g, writable: true);
                    if (k is null) continue;
                    var per = new Dictionary<string, OriginalValue>();
                    foreach (var n in Names)
                    {
                        var ex = k.GetValue(n);
                        per[n] = ex is null
                            ? new OriginalValue(false, null, null)
                            : new OriginalValue(true, k.GetValueKind(n).ToString(), ex.ToString());
                        k.SetValue(n, 1, RegistryValueKind.DWord);
                    }
                    originals[g] = per;
                }
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
            ctx.Trace($"applied {Id} on {originals.Count} interfaces");
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
                : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, OriginalValue>>>(json);
            using var root = Root;
            using var ifaces = root.OpenSubKey(InterfacesKey, writable: true);
            if (ifaces is null) return TweakResult.Ok();

            // если бэкапа нет — просто снимаем наши значения со всех интерфейсов
            var targets = originals?.Keys ?? (IEnumerable<string>)ifaces.GetSubKeyNames();
            foreach (var g in targets)
            {
                using var k = ifaces.OpenSubKey(g, writable: true);
                if (k is null) continue;
                foreach (var n in Names)
                {
                    if (originals is not null && originals.TryGetValue(g, out var per)
                        && per.TryGetValue(n, out var ov) && ov.Existed)
                        k.SetValue(n, Coerce(ov), RegistryValueKind.DWord);
                    else
                        k.DeleteValue(n, throwOnMissingValue: false);
                }
            }
            ctx.Trace($"reverted {Id}");
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static object Coerce(OriginalValue ov)
        => ov.Kind == RegistryValueKind.DWord.ToString() && int.TryParse(ov.Value, out var i)
            ? i
            : (object)(ov.Value ?? "");
}
