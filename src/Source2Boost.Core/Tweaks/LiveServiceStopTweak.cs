using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>
/// «Живое» отключение фоновых служб: тип запуска -> Disabled И немедленная остановка
/// (без перезагрузки). Параметризуется списком служб, чтобы одним классом закрыть
/// и SysMain/DiagTrack, и лишние службы (поиск/печать) как отдельные твики.
/// Точный откат: восстанавливает исходный тип запуска и, если служба была
/// авто-запускаемой, снова её стартует.
/// </summary>
public sealed class LiveServiceStopTweak : ITweak
{
    private readonly string[] _targets;

    public string Id { get; }
    public TweakCategory Category => TweakCategory.Services;
    public RiskLevel Risk { get; }
    public bool RequiresRestart => false;
    public L10n Title { get; }
    public L10n Description { get; }
    public L10n Impact { get; }

    public LiveServiceStopTweak(string id, RiskLevel risk, string[] targets, L10n title, L10n description, L10n impact)
    {
        Id = id; Risk = risk; _targets = targets;
        Title = title; Description = description; Impact = impact;
    }

    /// <summary>Штатный твик SysMain+DiagTrack (заменяет прежние reg-твики sysmain-off/diagtrack-off).</summary>
    public static LiveServiceStopTweak SysMainDiagTrack() => new(
        "service-live-stop", RiskLevel.Medium, new[] { "SysMain", "DiagTrack" },
        new L10n("Остановить фоновые службы", "Зупинити фонові служби", "Stop background services"),
        new L10n("SysMain и DiagTrack переводятся в Disabled и останавливаются прямо сейчас — без перезагрузки.",
                 "SysMain та DiagTrack переводяться в Disabled і зупиняються зараз — без перезавантаження.",
                 "SysMain and DiagTrack are set to Disabled and stopped right now — no reboot needed."),
        new L10n("-фоновая нагрузка", "-фонове навантаження", "-background load"));

    /// <summary>Доп. службы (паритет с cs2-omz): поиск Windows + очередь печати.</summary>
    public static LiveServiceStopTweak SearchAndSpooler() => new(
        "extra-services-off", RiskLevel.Medium, new[] { "WSearch", "Spooler" },
        new L10n("Отключить поиск и печать", "Вимкнути пошук і друк", "Disable Search & Print Spooler"),
        new L10n("Отключает индексатор поиска Windows (WSearch) и очередь печати (Spooler) — они постоянно крутятся в фоне. ВНИМАНИЕ: поиск в меню Пуск станет медленнее, а печать перестанет работать, пока не откатишь. Обратимо, без перезагрузки.",
                 "Вимикає індексатор пошуку Windows (WSearch) та чергу друку (Spooler) — вони постійно крутяться у фоні. УВАГА: пошук у меню Пуск стане повільнішим, а друк не працюватиме, доки не відкотиш. Оборотно, без перезавантаження.",
                 "Disables Windows Search indexer (WSearch) and the Print Spooler — both run constantly in the background. WARNING: Start-menu search gets slower and printing won't work until reverted. Reversible, no reboot."),
        new L10n("-фоновая нагрузка", "-фонове навантаження", "-background load"));

    /// <summary>Службы Xbox (паритет с CS2-Ultimate-Optimization) — CS2 не из Store, не нужны.</summary>
    public static LiveServiceStopTweak XboxServices() => new(
        "xbox-services-off", RiskLevel.Medium,
        new[] { "XblGameSave", "XboxGipSvc", "XboxNetApiSvc", "XblAuthManager" },
        new L10n("Отключить службы Xbox", "Вимкнути служби Xbox", "Disable Xbox services"),
        new L10n("Останавливает фоновые службы Xbox (сохранения, геймпад-провайдер, сетевое API, авторизация). CS2 ставится не из Microsoft Store, поэтому они не нужны. Обратимо.",
                 "Зупиняє фонові служби Xbox (збереження, геймпад-провайдер, мережеве API, авторизація). CS2 ставиться не з Microsoft Store, тож вони не потрібні. Оборотно.",
                 "Stops the background Xbox services (saves, gamepad provider, network API, auth). CS2 isn't a Store app, so they're unneeded. Reversible."),
        new L10n("-фоновая нагрузка", "-фонове навантаження", "-background load"));

    public bool IsSupported(TweakContext ctx) => true;

    public bool IsApplied(TweakContext ctx)
    {
        var any = false;
        foreach (var s in _targets)
        {
            var st = QueryStartType(s);
            if (st is null) continue;      // службы нет — не учитываем
            any = true;
            if (st != 4) return false;     // не Disabled -> ещё не применён
        }
        return any;
    }

    public TweakResult Apply(TweakContext ctx)
    {
        try
        {
            // Снимок исходного типа запуска фиксируем ТОЛЬКО ОДИН РАЗ на службу: повторный Apply
            // (реконсиляция/повторное применение профиля) не должен затирать истинный оригинал
            // уже-отключённым значением (иначе откат вернёт disabled/auto неправильно).
            var prior = ctx.Backup.LoadState(Id);
            var originals = prior is not null
                ? (JsonSerializer.Deserialize<Dictionary<string, int>>(prior) ?? new())
                : new Dictionary<string, int>();

            foreach (var s in _targets)
            {
                var st = QueryStartType(s);
                if (st is null) { ctx.Trace($"{Id}: service {s} not found, skip"); continue; }
                if (!originals.ContainsKey(s) && st.Value != 4)
                    originals[s] = st.Value;
                Run("sc.exe", $"config {s} start= disabled");
                Run("sc.exe", $"stop {s}");
                ctx.Trace($"{Id}: {s} disabled+stopped (was start={st})");
            }
            ctx.Backup.SaveState(Id, JsonSerializer.Serialize(originals));
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
            foreach (var s in _targets)
            {
                // если нет данных — считаем, что служба была авто (безопасный дефолт Windows)
                var orig = originals is not null && originals.TryGetValue(s, out var o) ? o : 2;
                Run("sc.exe", $"config {s} start= {StartToken(orig)}");
                if (orig == 2) Run("sc.exe", $"start {s}"); // авто -> снова поднять
                ctx.Trace($"{Id}: {s} restored to start={orig}");
            }
            return TweakResult.Ok();
        }
        catch (Exception ex) { return TweakResult.Fail(ex.Message); }
    }

    private static string StartToken(int st) => st switch
    {
        0 => "boot",
        1 => "system",
        2 => "auto",
        4 => "disabled",
        _ => "demand"
    };

    /// <summary>
    /// Тип запуска службы ИЗ РЕЕСТРА (HKLM\SYSTEM\CurrentControlSet\Services\&lt;svc&gt;\Start):
    /// 0=boot,1=system,2=auto,3=demand,4=disabled. Раньше парсили вывод <c>sc.exe qc</c> по
    /// англ. метке START_TYPE — но на локализованной (напр. русской) Windows sc печатает поле
    /// по-русски, regex не срабатывал, QueryStartType возвращал null → Apply МОЛЧА пропускал
    /// службы, а тумблер «моментально откатывался». Реестр locale-независим и надёжнее.
    /// </summary>
    private static int? QueryStartType(string svc)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc}");
            if (key is null) return null;                    // службы нет
            return key.GetValue("Start") is int i ? i : null;
        }
        catch { return null; }
    }

    private static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
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
