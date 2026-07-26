using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Source2Boost.App.Mvvm;
using Source2Boost.Core;

namespace Source2Boost.App.ViewModels;

/// <summary>Снимок посчитанных чисел панели «Тест и прогноз» — чтобы показать их мгновенно.</summary>
public sealed record ForecastSnapshot(int Score, int Applied, int Supported,
                                      int Cur, int Soft, int Bios, bool FromMeas);

/// <summary>
/// Владелец состояния и бизнес-логики окна (первый этап перевода на MVVM). Держит железо,
/// TweakContext, кэши «применён»/прогноза — вся не-UI работа переезжает сюда из code-behind,
/// чтобы её можно было тестировать и не смешивать с отрисовкой контролов.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private TweakContext? _ctx;
    private HardwareInfo? _hardware;
    private ForecastSnapshot? _forecast;

    /// <summary>Просканированное железо (null, пока идёт первичный детект).</summary>
    public HardwareInfo? Hardware
    {
        get => _hardware;
        set => SetProperty(ref _hardware, value);
    }

    /// <summary>Последний посчитанный прогноз FPS/оценка (для мгновенной отрисовки).</summary>
    public ForecastSnapshot? Forecast
    {
        get => _forecast;
        set => SetProperty(ref _forecast, value);
    }

    /// <summary>Контекст движка твиков (Backup + железо + лог). Создаётся лениво.</summary>
    public TweakContext Context
    {
        get
        {
            if (_ctx is null)
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Boost");
                _ctx = new TweakContext
                {
                    Hardware = _hardware ?? HardwareInfo.Detect(),
                    Backup = new BackupService(root),
                    Log = s => { System.Diagnostics.Debug.WriteLine("[S2B] " + s); Logger.Info(s); }
                };
            }
            return _ctx;
        }
    }

    /// <summary>Детект железа в фоне (WMI морозит UI). Возвращает и запоминает результат.</summary>
    public async Task<HardwareInfo> DetectHardwareAsync()
    {
        var hw = await Task.Run(HardwareInfo.Detect);
        Hardware = hw;
        return hw;
    }

    // ---------- Список «Твики» ----------

    /// <summary>Строки списка «Твики» (привязаны к ItemsControl в XAML).</summary>
    public ObservableCollection<TweakRowViewModel> Tweaks { get; } = new();

    /// <summary>Строки раздела «Лаборатория» — только экспериментальные твики (IExperimental).
    /// Живут ОТДЕЛЬНО от основного списка, чтобы эксперименты были в своём разделе.</summary>
    public ObservableCollection<TweakRowViewModel> LabTweaks { get; } = new();

    /// <summary>
    /// Строит список твиков МГНОВЕННО из кэша «применён», затем в фоне сверяет реальное
    /// состояние (параллельно) и поправляет тумблеры на месте + перезаписывает кэш.
    /// </summary>
    public async Task BuildTweaksAsync(string lang)
    {
        var ctx = Context;
        var cache = LoadAppliedCache();

        Tweaks.Clear();
        LabTweaks.Clear();
        var real = new List<TweakRowViewModel>();   // строки, участвующие в применении (не «скоро»)
        foreach (var tw in TweakCatalog.All())
        {
            bool soon = tw is IComingSoon;
            bool experimental = tw is IExperimental;
            bool supported; try { supported = soon || tw.IsSupported(ctx); } catch { supported = true; }
            if (!soon && !supported) continue;
            bool applied = !soon && cache.TryGetValue(tw.Id, out var on) && on;
            var row = new TweakRowViewModel(tw, soon, applied, lang);
            // Эксперименты — в свой раздел «Лаборатория», остальные — в основной список.
            if (experimental) LabTweaks.Add(row);
            else Tweaks.Add(row);
            if (!soon) real.Add(row);
        }

        var actual = await ComputeAppliedAsync(real.Select(r => r.Tweak));
        foreach (var row in real)
            if (actual.TryGetValue(row.Tweak.Id, out var on)) row.IsChecked = on;
        SaveAppliedCache(actual);
    }

    // ---------- Карточки-советы (read-only) ----------

    /// <summary>«Что можно улучшить» на дашборде (вердикт узкого места).</summary>
    public ObservableCollection<AdviceCardViewModel> Findings { get; } = new();

    /// <summary>Рекомендации по BIOS.</summary>
    public ObservableCollection<AdviceCardViewModel> BiosItems { get; } = new();

    // ---------- Список «Откат» ----------

    public ObservableCollection<RestoreRowViewModel> RestoreItems { get; } = new();

    /// <summary>Подпись рядом со спиннером загрузки списка.</summary>
    public string LoadingLabel => Loc.T("loading");

    private bool _restoreLoading;
    /// <summary>Крутить спиннер (первый заход без кэша).</summary>
    public bool RestoreLoading { get => _restoreLoading; private set => SetProperty(ref _restoreLoading, value); }

    private bool _restoreEmpty;
    /// <summary>Показать «пока ничего не применено».</summary>
    public bool RestoreEmpty { get => _restoreEmpty; private set => SetProperty(ref _restoreEmpty, value); }

    private bool _hasRestoreItems;
    /// <summary>Есть что откатывать (для доступности кнопки «Откатить всё»).</summary>
    public bool HasRestoreItems { get => _hasRestoreItems; private set => SetProperty(ref _hasRestoreItems, value); }

    /// <summary>
    /// Список «Откат»: мгновенно из кэша «применён» (тот же ui-applied-cache), спиннер только
    /// на первом заходе без кэша, затем фоновая сверка реального состояния + перезапись кэша.
    /// </summary>
    public async Task BuildRestoreAsync(string lang)
    {
        var ctx = Context;
        var cache = LoadAppliedCache();
        bool haveCache = cache.Count > 0;

        var cached = TweakCatalog.All()
            .Where(t => cache.TryGetValue(t.Id, out var on) && on)
            .Where(t => { try { return t.IsSupported(ctx); } catch { return false; } })
            .ToList();
        RestoreLoading = !haveCache;   // без кэша — крутим спиннер вместо пустого списка
        FillRestore(cached, lang);

        var real = await ComputeSupportedAppliedAsync(TweakCatalog.All());
        RestoreLoading = false;
        var applied = TweakCatalog.All().Where(t => real.TryGetValue(t.Id, out var on) && on).ToList();
        FillRestore(applied, lang);
        SaveAppliedCache(real);
    }

    private void FillRestore(List<ITweak> applied, string lang)
    {
        RestoreItems.Clear();
        foreach (var tw in applied)
            RestoreItems.Add(new RestoreRowViewModel(tw, lang, t => RevertOneAsync(t, lang)));
        HasRestoreItems = applied.Count > 0;
        RestoreEmpty = applied.Count == 0 && !RestoreLoading;
    }

    /// <summary>Откатить один твик и перестроить список.</summary>
    public async Task RevertOneAsync(ITweak tw, string lang)
    {
        var ctx = Context;
        await Task.Run(() => new Orchestrator(ctx).Revert(new[] { tw }));
        await BuildRestoreAsync(lang);
    }

    /// <summary>Откатить всё и перестроить список.</summary>
    public async Task RevertAllRestoreAsync(string lang)
    {
        var ctx = Context;
        await Task.Run(() => new Orchestrator(ctx).RevertAll());
        await BuildRestoreAsync(lang);
    }

    // ---------- Кэш «применён» (id→bool) ----------

    /// <summary>Кэш последнего известного «применён» — для мгновенной отрисовки тоглов/отката.</summary>
    public Dictionary<string, bool> LoadAppliedCache()
    {
        try
        {
            var j = Context.Backup.LoadState("ui-applied-cache");
            if (!string.IsNullOrWhiteSpace(j))
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(j) ?? new();
        }
        catch { }
        return new();
    }

    public void SaveAppliedCache(IDictionary<string, bool> state)
    {
        try { Context.Backup.SaveState("ui-applied-cache", System.Text.Json.JsonSerializer.Serialize(state)); }
        catch { }
    }

    /// <summary>Реальное состояние «применён» по всем твикам (параллельно, ~1 с). Только IsApplied.</summary>
    public Task<ConcurrentDictionary<string, bool>> ComputeAppliedAsync(IEnumerable<ITweak> tweaks)
    {
        var ctx = Context;
        var list = tweaks.ToList();
        return Task.Run(() =>
        {
            var d = new ConcurrentDictionary<string, bool>();
            System.Threading.Tasks.Parallel.ForEach(list, t =>
            { try { d[t.Id] = t.IsApplied(ctx); } catch { d[t.Id] = false; } });
            return d;
        });
    }

    /// <summary>Реальный набор «поддержан И применён» (для панели «Откат»).</summary>
    public Task<ConcurrentDictionary<string, bool>> ComputeSupportedAppliedAsync(IEnumerable<ITweak> tweaks)
    {
        var ctx = Context;
        var list = tweaks.ToList();
        return Task.Run(() =>
        {
            var d = new ConcurrentDictionary<string, bool>();
            System.Threading.Tasks.Parallel.ForEach(list, t =>
            { try { d[t.Id] = t.IsSupported(ctx) && t.IsApplied(ctx); } catch { d[t.Id] = false; } });
            return d;
        });
    }

    // ---------- Прогноз / оценка ----------

    public double LastAvgFps() => LoadFps("last-avg-fps");

    /// <summary>Измеренный 1% low из последнего замера (0 если не мерили) — основа ровного капа.</summary>
    public double LastLow1() => LoadFps("last-low1-fps");

    private double LoadFps(string key)
    {
        var s = Context.Backup.LoadState(key);
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>Считает оценку оптимизации и прогноз FPS в фоне, запоминает в Forecast и кэше.</summary>
    public async Task<ForecastSnapshot> ComputeForecastAsync()
    {
        var ctx = Context;
        var hw = _hardware ?? HardwareInfo.Detect();
        double avg = LastAvgFps();
        var snap = await Task.Run(() =>
        {
            var s = OptimizationScore.Compute(ctx);
            var f = FpsEstimator.Forecast(hw, s.Fraction, avg);
            return new ForecastSnapshot(s.Score, s.AppliedCount, s.SupportedCount,
                                        f.CurrentAvg, f.PotentialSoftwareAvg, f.PotentialWithBiosAvg,
                                        f.FromMeasurement);
        });
        Forecast = snap;
        SaveForecastSnapshot(snap);
        return snap;
    }

    public ForecastSnapshot? LoadForecastSnapshot()
    {
        try
        {
            var j = Context.Backup.LoadState("forecast-cache");
            var snap = string.IsNullOrWhiteSpace(j)
                ? null
                : System.Text.Json.JsonSerializer.Deserialize<ForecastSnapshot>(j);
            if (snap is not null) _forecast = snap;
            return snap;
        }
        catch { return null; }
    }

    public void SaveForecastSnapshot(ForecastSnapshot s)
    {
        try { Context.Backup.SaveState("forecast-cache", System.Text.Json.JsonSerializer.Serialize(s)); }
        catch { }
    }
}
