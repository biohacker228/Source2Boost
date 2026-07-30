using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Source2Boost.App.ViewModels;
using Source2Boost.Core;
using Wpf.Ui.Appearance;

namespace Source2Boost.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly Color AccentColor = Color.FromRgb(0xFF, 0x6A, 0x2B);

    /// <summary>Владелец состояния и бизнес-логики (MVVM). Code-behind остаётся тонкой View-обвязкой.</summary>
    private readonly ViewModels.MainViewModel _vm = new();

    /// <summary>Просканированное железо. Проксирует состояние во ViewModel (единый владелец).</summary>
    private HardwareInfo? _hw { get => _vm.Hardware; set => _vm.Hardware = value; }

    private Dictionary<string, UIElement> _panels = new();
    private Dictionary<string, Button> _navs = new();

    private TweakContext Ctx() => _vm.Context;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;   // привязки списков (ItemsControl) резолвятся против ViewModel
        ApplyAccent();

        _panels = new()
        {
            ["welcome"] = PanelWelcome,
            ["dash"] = PanelDash, ["boost"] = PanelBoost, ["tweaks"] = PanelTweaks,
            ["monitor"] = PanelMonitor, ["lab"] = PanelLab, ["cs2"] = PanelCs2, ["bios"] = PanelBios, ["restore"] = PanelRestore,
            ["settings"] = PanelSettings
        };
        _navs = new()
        {
            ["dash"] = NavDash, ["boost"] = NavBoost, ["tweaks"] = NavTweaks,
            ["monitor"] = NavMonitor, ["lab"] = NavLab, ["cs2"] = NavCs2, ["bios"] = NavBios, ["restore"] = NavRestore,
            ["settings"] = NavSettings
        };

        // Пустое состояние Лаборатории обновляется и при async-заполнении списка.
        _vm.LabTweaks.CollectionChanged += (_, _) => RefreshLab();

        CmbLang.SelectionChanged += (_, _) =>
            Loc.Set(CmbLang.SelectedIndex switch { 1 => "uk", 2 => "en", _ => "ru" });
        Loc.Changed += () => { ApplyTexts(); BuildTweaks(); };

        BtnTheme.Click += (_, _) => ApplyPalette(!_dark);

        Loaded += async (_, _) =>
        {
            if (TrySelfTest()) return;
            if (TryDumpCatalog()) return;
            if (TryApplyTest()) return;
            if (TryNvApiTest()) return;
            // Автозапуск при входе в Windows: стартуем свёрнутым (сторож CS2 работает в фоне).
            if (Environment.GetCommandLineArgs().Any(a => a.Equals("--autostarted", StringComparison.OrdinalIgnoreCase)))
                WindowState = WindowState.Minimized;

            // Окно уже на экране — детект железа (WMI, 1–3 с) уводим в фон, чтобы старт был
            // мгновенным. Пока идёт — крутим оверлей загрузки вместо застывшего пустого окна.
            PanelLoading.Visibility = Visibility.Visible;
            await _vm.DetectHardwareAsync();
            PanelLoading.Visibility = Visibility.Collapsed;

            // Эксперимент «держать таймер 0.5 мс» живёт в процессе — переустанавливаем запрос при старте.
            if (Ctx().Backup.LoadState(Core.TimerResHoldTweak.StateKey) == "1")
                Core.TimerResolution.RequestMax();

            ApplyTexts();
            // Первый запуск (ещё не сканировали через приветствие) → экран приветствия со сканом.
            bool welcomed = Ctx().Backup.LoadState("welcomed") == "1";
            if (welcomed) { BuildDashboard(); BuildTweaks(); }
            ShowPanel(welcomed ? "dash" : "welcome");
            MaybeSelfShot();
        };
    }

    /// <summary>Автотест движка на безопасном обратимом твике (--selftest=&lt;лог&gt;).</summary>
    private bool TrySelfTest()
    {
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--selftest=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        var logPath = arg["--selftest=".Length..].Trim('"');

        var sb = new StringBuilder();
        const string key = @"HKEY_CURRENT_USER\Software\Microsoft\GameBar";
        object? Read() => Microsoft.Win32.Registry.GetValue(key, "AutoGameModeEnabled", "<none>");
        try
        {
            _hw = HardwareInfo.Detect();
            var ctx = Ctx();
            var tw = TweakCatalog.All().First(t => t.Id == "game-mode");
            sb.AppendLine($"tweak: {tw.Id}");
            sb.AppendLine($"reg before      = {Read()}");
            sb.AppendLine($"IsApplied before= {tw.IsApplied(ctx)}");
            var a = tw.Apply(ctx);
            sb.AppendLine($"APPLY           = success:{a.Success} {a.Message}");
            sb.AppendLine($"reg after apply = {Read()}");
            sb.AppendLine($"IsApplied apply = {tw.IsApplied(ctx)}");
            var r = tw.Revert(ctx);
            sb.AppendLine($"REVERT          = success:{r.Success} {r.Message}");
            sb.AppendLine($"reg after revert= {Read()}");
            sb.AppendLine($"IsApplied revert= {tw.IsApplied(ctx)}");
            sb.AppendLine($"backup dir      = {ctx.Backup.SessionDir}");
        }
        catch (Exception ex) { sb.AppendLine("EXCEPTION: " + ex); }

        File.WriteAllText(logPath, sb.ToString());
        Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        return true;
    }

    /// <summary>Dev-выгрузка каталога: --catalog=&lt;файл&gt; пишет id | риск | применимость | применён.</summary>
    private bool TryDumpCatalog()
    {
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--catalog=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        var path = arg["--catalog=".Length..].Trim('"');
        var sb = new StringBuilder();
        try
        {
            _hw = HardwareInfo.Detect();
            var ctx = Ctx();
            sb.AppendLine($"RAM={_hw.RamGb}GB threads={_hw.CpuThreads} discreteGPU={_hw.HasDiscreteGpu} vendor={_hw.GpuVendor} monitors={_hw.MonitorCount}");
            sb.AppendLine("id | risk | supported | applied");
            foreach (var t in TweakCatalog.All())
            {
                bool sup; try { sup = t.IsSupported(ctx); } catch { sup = false; }
                bool app = false; try { app = sup && t.IsApplied(ctx); } catch { }
                sb.AppendLine($"{t.Id,-28} {t.Risk,-8} sup={sup,-5} applied={app}");
            }
            foreach (var p in new[] { Profile.Safe, Profile.Optimal, Profile.Maximum })
                sb.AppendLine($"profile {p}: {TweakCatalog.ForProfile(p, ctx).Count()} tweaks");
        }
        catch (Exception ex) { sb.AppendLine("EXCEPTION: " + ex); }
        File.WriteAllText(path, sb.ToString());
        Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        return true;
    }

    /// <summary>Dev: --apply-test=&lt;файл&gt; — прогоняет РЕАЛЬНЫЙ Apply→Revert твика служб через
    /// боевой код (elevated) и пишет трассу sc + IsApplied до/после. Система остаётся чистой.</summary>
    private bool TryApplyTest()
    {
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--apply-test=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        var path = arg["--apply-test=".Length..].Trim('"');
        var sb = new StringBuilder();
        try
        {
            _hw = HardwareInfo.Detect();
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Source2Boost");
            var ctx = new TweakContext
            {
                Hardware = _hw,
                Backup = new BackupService(root),
                Log = s => sb.AppendLine("  trace: " + s)
            };
            var tw = TweakCatalog.All().First(t => t.Id == "service-live-stop");
            sb.AppendLine($"IsApplied before      = {tw.IsApplied(ctx)}");
            var r = tw.Apply(ctx);
            sb.AppendLine($"Apply: success={r.Success} msg={r.Message}");
            sb.AppendLine($"IsApplied after apply = {tw.IsApplied(ctx)}");
            var rv = tw.Revert(ctx);
            sb.AppendLine($"Revert: success={rv.Success} msg={rv.Message}");
            sb.AppendLine($"IsApplied after revert= {tw.IsApplied(ctx)}");
        }
        catch (Exception ex) { sb.AppendLine("EXCEPTION: " + ex); }
        File.WriteAllText(path, sb.ToString());
        Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        return true;
    }

    /// <summary>Dev: --nvapi-test=&lt;файл&gt; — неразрушающая проверка NVAPI на реальной карте.</summary>
    private bool TryNvApiTest()
    {
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--nvapi-test=", StringComparison.OrdinalIgnoreCase));
        if (arg is null) return false;
        var path = arg["--nvapi-test=".Length..].Trim('"');
        try { File.WriteAllText(path, NvApi.Diag()); } catch { }
        Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
        return true;
    }

    private void ApplyAccent() =>
        ApplicationAccentColorManager.Apply(_dark ? AccentColor : Color.FromRgb(0xE8, 0x5D, 0x1A),
                                            ApplicationThemeManager.GetAppTheme());

    /// <summary>Закрытие окна не завершает приложение, а сворачивает в трей (сторож CS2 продолжает
    /// работать в фоне). Реальный выход — из меню значка в трее («Выход»).</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current is App app && app.HandleMainWindowClosing()) { e.Cancel = true; return; }
        base.OnClosing(e);
    }

    // ---------- Тема: живая смена тёплой палитры ----------
    private bool _dark = true;

    private static Color Hex(string h)
    {
        h = h.TrimStart('#');
        byte B(int i) => Convert.ToByte(h.Substring(i, 2), 16);
        return h.Length == 8 ? Color.FromArgb(B(0), B(2), B(4), B(6))
                             : Color.FromRgb(B(0), B(2), B(4));
    }

    private void SetBrush(string key, string hex)
    {
        // XAML-кисти заморожены — не мутируем, а подменяем запись; её подхватит DynamicResource.
        Resources[key] = new SolidColorBrush(Hex(hex));
    }

    /// <summary>Меняет цвет именованных кистей вживую (StaticResource держит один инстанс).</summary>
    private void ApplyPalette(bool dark)
    {
        _dark = dark;
        if (dark)
        {
            SetBrush("Bg", "#14110D");   SetBrush("Side", "#17130D"); SetBrush("Surface", "#1D1811");
            SetBrush("Card", "#241E16"); SetBrush("Line", "#332B20");
            // Muted осветлён с #8D8069: тот давал 4.26:1 на карточке — ниже нормы 4.5:1
            // для мелкого текста (подписи карточек, чипы). #988A72 = 4.88 на Card, 5.47 на Side.
            SetBrush("Text", "#F3EDE2"); SetBrush("Text2", "#BCAE9B"); SetBrush("Muted", "#988A72");
            SetBrush("Accent", "#FF6A2B"); SetBrush("AccentSoft", "#33FF6A2B");
            SetBrush("Good", "#45D68A"); SetBrush("Warn", "#F5C53D"); SetBrush("Crit", "#FF6B6F");
        }
        else
        {
            SetBrush("Bg", "#F7F1E7");   SetBrush("Side", "#F0E7D8"); SetBrush("Surface", "#FCF8F1");
            SetBrush("Card", "#FFFFFF"); SetBrush("Line", "#E4D8C5");
            // Muted затемнён с #9A8C74: тот давал всего 3.29:1 на белой карточке — заметно ниже
            // нормы. #71654E = 5.72 на Card, 4.66 на боковой панели.
            SetBrush("Text", "#26201A"); SetBrush("Text2", "#6E6150"); SetBrush("Muted", "#71654E");
            SetBrush("Accent", "#E85D1A"); SetBrush("AccentSoft", "#22E85D1A");
            SetBrush("Good", "#1F9D57"); SetBrush("Warn", "#C0850A"); SetBrush("Crit", "#DC4448");
        }
        ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        ApplyAccent();
        RecolorNav();            // навигация держит резолвнутые кисти — перекрасить под новую тему
        RefreshBoostHighlight(); // подписи/подсветка планов тоже используют резолвнутые кисти
    }

    // ---------- Навигация ----------
    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id) ShowPanel(id);
    }

    private string _activePanelId = "dash";

    private void ShowPanel(string id)
    {
        foreach (var (key, panel) in _panels)
            panel.Visibility = key == id ? Visibility.Visible : Visibility.Collapsed;

        _activePanelId = id;
        RecolorNav();

        if (id == "restore") BuildRestore();
        else if (id == "cs2") RefreshCs2();
        else if (id == "boost") RefreshBoostHighlight();
        else if (id == "bios") BuildBios();
        else if (id == "monitor") _ = RefreshForecast();
        else if (id == "dash") BuildDashboard();
        else if (id == "lab") RefreshLab();
        else if (id == "settings") RefreshAutomation();
    }

    /// <summary>Перекрасить пункты навигации ТЕКУЩИМИ кистями темы. Важно вызывать после смены темы:
    /// цвета навигации задаются резолвнутыми кистями (не DynamicResource), иначе при переключении на
    /// светлую тему пункты оставались бы старым тёмным цветом и «пропадали» на светлом фоне.</summary>
    private void RecolorNav()
    {
        if (_navs is null) return;
        var soft = (Brush)FindResource("AccentSoft");
        var accent = (Brush)FindResource("Accent");
        var t2 = (Brush)FindResource("Text2");
        foreach (var (key, nav) in _navs)
        {
            bool on = key == _activePanelId;
            nav.Background = on ? soft : Brushes.Transparent;
            nav.Foreground = on ? accent : t2;
        }
    }

    // ---------- Сканирование ----------
    private void Scan_Click(object sender, RoutedEventArgs e) => Scan();

    private void Scan()
    {
        _hw = HardwareInfo.Detect();
        ApplyTexts();
        BuildDashboard();
    }

    /// <summary>Последний замер (для вердикта узкого места по GPU-busy).</summary>
    private FrametimeStats? _lastStats;

    /// <summary>Активное непрерывное слежение авто-замера (второй клик по кнопке = остановить).</summary>
    private System.Threading.CancellationTokenSource? _benchCts;

    // ---------- Первый запуск: скан + отчёт ----------
    private async void WelcomeScan_Click(object sender, RoutedEventArgs e)
    {
        BtnWelcomeScan.IsEnabled = false;
        WelcomeProgress.Visibility = Visibility.Visible;
        TxtWelcomeStatus.Text = Loc.T("welcome.scanning");
        await Task.Delay(150);
        _hw = await Task.Run(HardwareInfo.Detect);   // детект в фоне (WMI не морозит UI)
        await Task.Delay(650);                        // короткая пауза, чтобы анимация читалась
        Ctx().Backup.SaveState("welcomed", "1");
        ApplyTexts();
        BuildDashboard();
        BtnWelcomeScan.IsEnabled = true;
        WelcomeProgress.Visibility = Visibility.Collapsed;
        ShowPanel("dash");
    }

    /// <summary>Строит «Диагноз» (вердикт узкого места) и список «Что можно улучшить».</summary>
    private void BuildDashboard()
    {
        if (_hw is null) return;
        var v = BottleneckAnalyzer.Analyze(_hw, _lastStats);

        TxtDiagTitle.Text = v.Headline.For(Loc.Lang);
        TxtDiagMsg.Text = v.Summary.For(Loc.Lang);

        // Список «Что можно улучшить» привязан к _vm.Findings (ItemsControl + DataTemplate).
        _vm.Findings.Clear();
        foreach (var f in v.Findings)
            _vm.Findings.Add(AdviceCardViewModel.FromLevel(f.Level, f.Title, f.Detail, Loc.Lang));
        TxtFindingsHdr.Visibility = v.Findings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        RefreshBoostMode();
    }

    /// <summary>«Игровой фокус»: усыпить фон / разбудить обратно.</summary>
    private async void BoostToggle_Click(object sender, RoutedEventArgs e)
    {
        if (BoostToggle.IsChecked == true)
        {
            BoostToggle.IsEnabled = false;
            var (count, names) = await Task.Run(GameBoostService.Boost);
            BoostToggle.IsEnabled = true;
            TxtBoostModeStatus.Visibility = Visibility.Visible;
            TxtBoostModeStatus.Text = count > 0
                ? string.Format(Loc.T("boostmode.on"), count) + (string.IsNullOrEmpty(names) ? "" : $" ({names})")
                : Loc.T("boostmode.none");
        }
        else
        {
            BoostToggle.IsEnabled = false;
            var n = await Task.Run(GameBoostService.Restore);
            BoostToggle.IsEnabled = true;
            TxtBoostModeStatus.Visibility = Visibility.Visible;
            TxtBoostModeStatus.Text = string.Format(Loc.T("boostmode.off"), n);
        }
    }

    /// <summary>Синхронизирует тумблер Boost с реальным состоянием (пережил перезапуск окна).</summary>
    private void RefreshBoostMode()
    {
        if (BoostToggle is null) return;
        // Если включён авто-режим (фокус применяется сам при старте CS2) — показываем ползунок
        // ВКЛючённым, чтобы у пользователя не было желания жать его вручную каждый раз.
        bool autoGame = Ctx().Backup.LoadState("auto-game") == "1";
        BoostToggle.IsChecked = GameBoostService.IsBoosted || autoGame;
        if (GameBoostService.IsBoosted)
        {
            TxtBoostModeStatus.Visibility = Visibility.Visible;
            TxtBoostModeStatus.Text = Loc.T("boostmode.active");
        }
        else if (autoGame)
        {
            TxtBoostModeStatus.Visibility = Visibility.Visible;
            TxtBoostModeStatus.Text = Loc.T("boostmode.auto");
        }
        RefreshAutomation();
    }

    // ---------- Автоматизация: автозапуск + сторож CS2 ----------
    private DispatcherTimer? _cs2Watch;
    private bool _cs2WasRunning;

    private void RefreshAutomation()
    {
        if (AutoStartToggle is null) return;
        AutoStartToggle.IsChecked = AutoStartService.IsEnabled();
        AutoGameToggle.IsChecked = Ctx().Backup.LoadState("auto-game") == "1";
        RefreshPro();
        EnsureWatcher();
        if (TxtUpdateVersion is not null)
            TxtUpdateVersion.Text = $"{Loc.T("update.version")} {Core.UpdateService.CurrentVersion.ToString(3)}";
    }

    /// <summary>Проверить фид обновлений и, при согласии пользователя, скачать+запустить новый установщик.</summary>
    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (BtnCheckUpdate is null || TxtUpdateStatus is null) return;
        BtnCheckUpdate.IsEnabled = false;
        TxtUpdateStatus.Text = Loc.T("update.checking");
        try
        {
            var info = await Core.UpdateService.CheckAsync();
            if (info is null)
            {
                TxtUpdateStatus.Text = Loc.T("update.uptodate");
                return;
            }

            TxtUpdateStatus.Text = string.Format(Loc.T("update.available"), info.Version.ToString(3));
            var ask = await ShowConfirmDialog(Loc.T("nav.settings"),
                string.Format(Loc.T("update.confirm"), info.Version.ToString(3)) +
                (string.IsNullOrWhiteSpace(info.Notes) ? "" : $"\n\n{info.Notes}"));
            if (!ask) return;

            var progress = new Progress<double>(p =>
                TxtUpdateStatus.Text = string.Format(Loc.T("update.downloading"), (int)(p * 100)));
            var setup = await Core.UpdateService.DownloadAsync(info, progress);

            // Запускаем установщик и выходим, чтобы он смог заменить файлы.
            Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // Код в статусе — чтобы пользователь мог прислать его, не открывая журнал.
            TxtUpdateStatus.Text = $"{Loc.T("update.error")} ({Core.Logger.ErrorCode("update", ex)})";
        }
        finally
        {
            if (BtnCheckUpdate is not null) BtnCheckUpdate.IsEnabled = true;
        }
    }

    private async void AutoStartToggle_Click(object sender, RoutedEventArgs e)
    {
        bool want = AutoStartToggle.IsChecked == true;
        bool ok = want ? AutoStartService.Enable() : AutoStartService.Disable();
        if (!ok)
        {
            AutoStartToggle.IsChecked = !want; // откатить визуально
            await ShowInfoDialog(Loc.T("dash.title"), Loc.T("auto.start.err"));
        }
    }

    private async void AutoGameToggle_Click(object sender, RoutedEventArgs e)
    {
        // Авто-режим — функция Pro.
        if (AutoGameToggle.IsChecked == true && !Core.LicenseService.IsPro)
        {
            AutoGameToggle.IsChecked = false;
            await ShowInfoDialog(Loc.T("pro.title"), Loc.T("pro.automode"));
            return;
        }
        Ctx().Backup.SaveState("auto-game", AutoGameToggle.IsChecked == true ? "1" : "0");
        EnsureWatcher();
        RefreshBoostMode();   // ползунок игрового фокуса должен сразу отразить авто-режим
    }

    /// <summary>Активация Pro промокодом.</summary>
    private void ActivatePromo_Click(object sender, RoutedEventArgs e)
    {
        bool ok = Core.LicenseService.TryActivate(TxtPromo.Text);
        TxtProResult.Visibility = Visibility.Visible;
        TxtProResult.Foreground = (System.Windows.Media.Brush)FindResource(ok ? "Good" : "Crit");
        TxtProResult.Text = Loc.T(ok ? "pro.promo.ok" : "pro.promo.bad");
        if (ok)
        {
            TxtPromo.Text = "";
            RefreshPro();
            BuildTweaks();       // разблокировать твики и Лабораторию
            RefreshBoostMode();
        }
    }

    /// <summary>Обновить блок Pro в настройках (чип тарифа, статус триала, видимость промокода).</summary>
    private void RefreshPro()
    {
        if (TxtProTier is null) return;
        bool activated = Core.LicenseService.IsActivated;
        TxtProTier.Text = Loc.T(Core.LicenseService.IsPro ? "pro.tier.pro" : "pro.tier.free");
        TxtProTrial.Text = activated
            ? Loc.T("pro.active")
            : (Core.LicenseService.TrialActive ? string.Format(Loc.T("pro.trial"), Core.LicenseService.TrialDaysLeft) : "");
        RowPromo.Visibility = activated ? Visibility.Collapsed : Visibility.Visible;
        TxtProPromoHint.Visibility = activated ? Visibility.Collapsed : Visibility.Visible;
#if DEBUG
        RowDebugTier.Visibility = Visibility.Visible;
        DebugTierToggle.IsChecked = Core.LicenseService.IsPro;
#endif
    }

    private void DebugTierToggle_Click(object sender, RoutedEventArgs e)
    {
#if DEBUG
        Core.LicenseService.DebugOverride = DebugTierToggle.IsChecked == true ? Core.Tier.Pro : Core.Tier.Free;
        RefreshPro(); BuildTweaks(); RefreshLab(); RefreshBoostMode();
#endif
    }

    /// <summary>Сторож CS2 нужен, если включён Авто-режим ИЛИ твик аффинити CS2 (его надо
    /// переприменять на старте игры). Запускает/останавливает таймер соответственно.</summary>
    private void EnsureWatcher()
    {
        bool affinity = Ctx().Backup.LoadState(Cs2AffinityTweak.StateKey) == "1";
        bool noCore0 = Ctx().Backup.LoadState(Cs2NoCore0Tweak.StateKey) == "1";
        bool on = AutoGameToggle?.IsChecked == true || affinity || noCore0;
        if (on && _cs2Watch is null)
        {
            _cs2WasRunning = PresentMonService.IsCs2Running();
            _cs2Watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            _cs2Watch.Tick += Cs2Watch_Tick;
            _cs2Watch.Start();
        }
        else if (!on && _cs2Watch is not null)
        {
            _cs2Watch.Stop(); _cs2Watch = null;
        }
    }

    /// <summary>Раз в ~12с: ловим запуск/выход CS2 (авто-режим) и переприменяем аффинити.</summary>
    private async void Cs2Watch_Tick(object? sender, EventArgs e)
    {
        bool running = PresentMonService.IsCs2Running();
        bool autoGame = AutoGameToggle?.IsChecked == true;

        if (running != _cs2WasRunning)
        {
            _cs2WasRunning = running;
            if (running && autoGame)
            {
                Logger.Info("watch: CS2 started -> auto boost + enforce");
                // 1) Переприменить последний профиль (службы, что Windows могла сбросить).
                var lastProfile = Ctx().Backup.LoadState("last-profile");
                if (Enum.TryParse<Profile>(lastProfile, out var prof))
                {
                    var ctx = Ctx();
                    await Task.Run(() => new Orchestrator(ctx).Apply(TweakCatalog.ForProfile(prof, ctx), createRestorePoint: false));
                }
                // 2) Включить Игровой фокус.
                if (!GameBoostService.IsBoosted) await Task.Run(GameBoostService.Boost);
                RefreshBoostMode();
            }
            else if (!running && autoGame)
            {
                Logger.Info("watch: CS2 exited -> restore background");
                if (GameBoostService.IsBoosted) await Task.Run(GameBoostService.Restore);
                RefreshBoostMode();
            }
        }

        // Аффинити переприменяем КАЖДЫЙ тик, пока CS2 живёт: движок иногда сбрасывает маску.
        // Общая маска учитывает оба твика сразу (топология + «без ЦП0»), чтобы не конфликтовали.
        bool topo = Ctx().Backup.LoadState(Cs2AffinityTweak.StateKey) == "1";
        bool noCore0 = Ctx().Backup.LoadState(Cs2NoCore0Tweak.StateKey) == "1";
        if (running && (topo || noCore0))
        {
            var mask = Cs2Affinity.DesiredMask(_hw, topo, noCore0);
            if (mask != 0) await Task.Run(() => Cs2Affinity.Apply(mask));
        }
    }

    private bool _busy;

    // Free — только безопасный профиль (агрессивное/оптимальное = Pro); Pro — полный «Оптимальный».
    private void Optimize_Click(object sender, RoutedEventArgs e)
        => _ = RunProfile(Core.LicenseService.IsPro ? Profile.Optimal : Profile.Safe);
    private void ProfSafe_Click(object sender, RoutedEventArgs e) => _ = RunProfile(Profile.Safe);
    private void ProfOptimal_Click(object sender, RoutedEventArgs e) => _ = RunProfile(Profile.Optimal);
    private void ProfMax_Click(object sender, RoutedEventArgs e) => _ = RunProfile(Profile.Maximum);

    private static string ProfileNameKey(Profile p) => p switch
    {
        Profile.Safe => "profile.safe",
        Profile.Optimal => "profile.optimal",
        _ => "profile.max",
    };

    private async Task RunProfile(Profile profile)
    {
        if (_busy) return;
        // В Free доступен только безопасный профиль; «Оптимальный»/«Максимум» — Pro.
        if (profile != Profile.Safe && !Core.LicenseService.IsPro)
        {
            await ShowInfoDialog(Loc.T("pro.title"), Loc.T("pro.profile"));
            return;
        }
        var tweaks = TweakCatalog.ForProfile(profile, Ctx()).ToList();
        var confirm = string.Format(Loc.T("opt.confirm"), Loc.T(ProfileNameKey(profile)), Loc.Tweaks(tweaks.Count));
        if (tweaks.Any(t => t.RequiresRestart)) confirm += Loc.T("opt.reboot");
        if (!await ShowConfirmDialog(Loc.T("opt.title"), confirm)) return;

        _busy = true;
        BtnOptimize.IsEnabled = false;
        TxtOptimize.Text = Loc.T("opt.working");
        if (TxtBoostStatus is not null) TxtBoostStatus.Text = Loc.T("opt.working");

        var ctx = Ctx();
        var results = await Task.Run(() => new Orchestrator(ctx).ApplyProfile(profile));
        ctx.Backup.SaveState("last-profile", profile.ToString());

        _busy = false;
        BtnOptimize.IsEnabled = true;
        TxtOptimize.Text = Loc.T("dash.optimize");
        RefreshBoostHighlight();
        BuildTweaks();
        _ = RefreshForecast();   // пересчитать оценку/прогноз после применения

        int ok = results.Count(r => r.Result.Success);
        var msg = string.Format(Loc.T("opt.done"), ok, results.Count);
        if (TxtBoostStatus is not null) TxtBoostStatus.Text = msg.Replace("\n", "  ");
        await ShowInfoDialog(Loc.T("opt.title"), msg);
    }

    /// <summary>Описание плана: «…текст. 21 твик.» + маркер перезагрузки, если план её требует.</summary>
    private string ProfileDesc(string key, Profile p)
    {
        var n = TweakCatalog.ForProfile(p, Ctx()).Count();
        var text = string.Format(Loc.T(key), Loc.Tweaks(n));
        if (TweakCatalog.ForProfile(p, Ctx()).Any(t => t.RequiresRestart))
            text += "  " + Loc.T("profile.reboot");
        return text;
    }

    /// <summary>
    /// Подсветка планов. Обводка карточки — на применённом профиле (если ещё не применяли —
    /// на «Оптимальном» как рекомендации). Оранжевая кнопка «Активно» — ТОЛЬКО на реально
    /// применённом плане; на остальных обычная кнопка «Применить».
    /// </summary>
    private void RefreshBoostHighlight()
    {
        var saved = Ctx().Backup.LoadState("last-profile")?.Trim();
        Profile? applied = saved switch
        {
            "Safe" => Profile.Safe,
            "Optimal" => Profile.Optimal,
            "Maximum" => Profile.Maximum,
            _ => null,
        };
        // Рекомендация профиля — по железу (классификатор), а не жёстко «Оптимальный».
        var reco = HardwareClassifier.Classify(_hw);
        var cardSel = applied ?? reco.RecommendedProfile;
        UpdateHwDiag(reco);

        var accent = (Brush)FindResource("Accent");
        var line = (Brush)FindResource("Line");
        var text = (Brush)FindResource("Text");
        var grad = (Brush)FindResource("AccentGrad");

        var rows = new (System.Windows.Controls.Border card, System.Windows.Controls.Button btn, System.Windows.Controls.TextBlock lbl, Profile p)[]
        {
            (CardProfSafe, BtnProfSafe, TxtProfSafeBtn, Profile.Safe),
            (CardProfOptimal, BtnProfOptimal, TxtProfOptimalBtn, Profile.Optimal),
            (CardProfMax, BtnProfMax, TxtProfMaxBtn, Profile.Maximum),
        };
        foreach (var (card, btn, lbl, p) in rows)
        {
            bool cardOn = p == cardSel;
            card.BorderBrush = cardOn ? accent : line;
            card.BorderThickness = new Thickness(cardOn ? 1.5 : 1);

            bool active = applied == p;             // «Активно» только если реально применён
            if (active)
            {
                btn.Background = grad;
                btn.BorderThickness = new Thickness(0);
                btn.Foreground = Brushes.White;
                lbl.Text = Loc.T("profile.active");
            }
            else
            {
                btn.Background = Brushes.Transparent;
                btn.BorderBrush = line;
                btn.BorderThickness = new Thickness(1.5);
                btn.Foreground = text;
                lbl.Text = Loc.T("profile.apply");
            }
        }
    }

    /// <summary>Заполнить баннер-диагноз «профиль подобран под ваше железо» текстом классификатора.</summary>
    private void UpdateHwDiag(HardwareClassifier.Result reco)
    {
        if (TxtHwDiagTitle is null) return;
        var recName = Loc.T(ProfileNameKey(reco.RecommendedProfile));
        TxtHwDiagTitle.Text = string.Format(Loc.T("hw.diag.title"), recName);
        TxtHwDiagBody.Text = reco.Headline.For(Loc.Lang);
        TxtHwDiagFocus.Text = reco.Focus.For(Loc.Lang);
        // Инлайн-бейдж «рекомендуется» на карточке «Оптимальный» — только если он и рекомендован
        // (иначе противоречил бы баннеру, который может советовать «Максимум» на слабом ПК).
        if (BadgeRecoOptimal is not null)
            BadgeRecoOptimal.Visibility = reco.RecommendedProfile == Profile.Optimal
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ApplySelected_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var ctx = Ctx();
        // Снимок состояния тумблеров из VM (обычный bool, но снимаем на UI-потоке до Task.Run).
        var snapshot = _vm.Tweaks.Where(r => !r.IsSoon)
            .Select(r => (tw: r.Tweak, on: r.IsChecked)).ToList();
        _busy = true;
        BtnApplySelected.IsEnabled = false;
        var (applied, reverted) = await Task.Run(() =>
        {
            var orch = new Orchestrator(ctx);
            var toApply = snapshot.Where(r => r.on && r.tw.IsSupported(ctx) && !r.tw.IsApplied(ctx))
                              .Select(r => r.tw).ToList();
            var toRevert = snapshot.Where(r => !r.on && r.tw.IsSupported(ctx) && r.tw.IsApplied(ctx))
                               .Select(r => r.tw).ToList();
            int a = toApply.Count > 0 ? orch.Apply(toApply, createRestorePoint: true).Count(x => x.Result.Success) : 0;
            int rv = toRevert.Count > 0 ? orch.Revert(toRevert).Count(x => x.Result.Success) : 0;
            return (a, rv);
        });
        _busy = false;
        BtnApplySelected.IsEnabled = true;
        BuildTweaks(); // перечитать реальное состояние в тоглы
        EnsureWatcher(); // мог включиться/выключиться твик аффинити CS2 → (пере)запустить сторож
        await ShowInfoDialog(Loc.T("tweaks.title"), string.Format(Loc.T("tweaks.reconcile"), applied, reverted));
    }

    // ---------- Лаборатория ----------
    private void RefreshLab()
    {
        // Лаборатория — функция Pro. В Free показываем апселл вместо экспериментов и бенч-CTA.
        bool pro = Core.LicenseService.IsPro;
        if (CardLabUpsell is not null)
        {
            CardLabUpsell.Visibility = pro ? Visibility.Collapsed : Visibility.Visible;
            if (!pro) TxtLabUpsell.Text = Loc.T("pro.lab.locked");
        }
        if (CardLabBenchCta is not null) CardLabBenchCta.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;
        if (LabList is not null) LabList.Visibility = pro ? Visibility.Visible : Visibility.Collapsed;
        if (TxtLabEmpty is not null)
            TxtLabEmpty.Visibility = (pro && _vm.LabTweaks.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LabToMonitor_Click(object sender, RoutedEventArgs e) => ShowPanel("monitor");

    /// <summary>Применить/откатить экспериментальные твики Лаборатории (та же логика, что и «Твики»,
    /// но по коллекции LabTweaks — эксперименты живут отдельно от профилей).</summary>
    private async void ApplyLab_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var ctx = Ctx();
        var snapshot = _vm.LabTweaks.Where(r => !r.IsSoon)
            .Select(r => (tw: r.Tweak, on: r.IsChecked)).ToList();
        _busy = true;
        BtnApplyLab.IsEnabled = false;
        var (applied, reverted) = await Task.Run(() =>
        {
            var orch = new Orchestrator(ctx);
            var toApply = snapshot.Where(r => r.on && r.tw.IsSupported(ctx) && !r.tw.IsApplied(ctx))
                              .Select(r => r.tw).ToList();
            var toRevert = snapshot.Where(r => !r.on && r.tw.IsSupported(ctx) && r.tw.IsApplied(ctx))
                               .Select(r => r.tw).ToList();
            int a = toApply.Count > 0 ? orch.Apply(toApply, createRestorePoint: true).Count(x => x.Result.Success) : 0;
            int rv = toRevert.Count > 0 ? orch.Revert(toRevert).Count(x => x.Result.Success) : 0;
            return (a, rv);
        });
        _busy = false;
        BtnApplyLab.IsEnabled = true;
        BuildTweaks(); // перечитать реальное состояние в тоглы
        await ShowInfoDialog(Loc.T("lab.title"), string.Format(Loc.T("tweaks.reconcile"), applied, reverted));
    }

    /// <summary>Диалог ошибки с КОДОМ. Пользователь присылает код — по нему в журнале сразу
    /// находится строка с полным стеком (код = отметка времени записи).</summary>
    private static async Task ShowErrorDialog(string where, Exception ex)
    {
        var code = Core.Logger.ErrorCode(where, ex);
        await ShowInfoDialog(Loc.T("error.title"),
            string.Format(Loc.T("error.body"), code, ex.Message, Core.Logger.LogPath));
    }

    /// <summary>Тематический диалог WPF-UI (в стиле приложения) вместо системного MessageBox.</summary>
    private static async Task ShowInfoDialog(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            PrimaryButtonText = string.Empty,   // пустая строка прячет первичную кнопку → одна «OK»
        };
        await box.ShowDialogAsync();
    }

    /// <summary>Тематический диалог подтверждения (Да/Нет). true = пользователь согласился.</summary>
    private static async Task<bool> ShowConfirmDialog(string title, string message)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = Loc.T("dlg.yes"),
            CloseButtonText = Loc.T("dlg.no"),
        };
        return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private async void RevertAll_Click(object sender, RoutedEventArgs e)
    {
        var ctx = Ctx();
        // Один клик отменяет всю проделанную работу — спрашиваем подтверждение.
        if (!await ShowConfirmDialog(Loc.T("tweaks.title"), Loc.T("tweaks.revertall.confirm"))) return;
        BtnRevertAll.IsEnabled = false;
        var results = await Task.Run(() => new Orchestrator(ctx).RevertAll());
        BtnRevertAll.IsEnabled = true;
        int ok = results.Count(r => r.Result.Success);
        await ShowInfoDialog(Loc.T("tweaks.title"), string.Format(Loc.T("tweaks.reverted"), ok));
    }

    // ---------- Экран CS2 ----------
    private void RefreshCs2()
    {
        TxtLaunch.Text = Cs2Config.LaunchOptionsFor(_hw);
        var exe = Cs2Paths.Cs2ExePath();
        ValCs2Path.Text = exe ?? Loc.T("cs2.notfound");
        var ap = Cs2Config.AutoexecPath();
        TxtAutoexecStatus.Text = Cs2Config.IsAutoexecInstalled()
            ? string.Format(Loc.T("cs2.installed"), ap)
            : (ap is null ? Loc.T("cs2.notfound") : Loc.T("cs2.notinstalled"));
        BtnInstallAutoexec.IsEnabled = ap is not null;

        // Рекомендация лимита FPS: у стабильного потолка по СПОСОБНОСТИ железа —
        // реальный замер, иначе прогноз (герцовка на кап не влияет, см. RecommendFpsCap).
        double predicted = _hw is null ? 0 : FpsEstimator.HeuristicBaseline(_hw);
        // Если есть измеренный 1% low — кап считаем от него (ровнее пейсинг), иначе от среднего/прогноза.
        int rec = Cs2Config.RecommendFpsCap(_vm.LastAvgFps(), predicted, _vm.LastLow1());
        // Своё значение пользователя имеет приоритет и переживает перезапуск приложения.
        var saved = Ctx().Backup.LoadState("fps-cap");
        if (!string.IsNullOrWhiteSpace(saved)) TxtFpsCap.Text = saved;
        else if (string.IsNullOrWhiteSpace(TxtFpsCap.Text)) TxtFpsCap.Text = rec.ToString();
        // Подсказка объясняет базу расчёта: если мерили 1% low — говорим об этом честно.
        TxtFpsHint.Text = string.Format(Loc.T(_vm.LastLow1() > 0 ? "cs2.fps.hint.low1" : "cs2.fps.hint"), rec);

        RefreshVideoConfig();
    }

    /// <summary>Запоминает свой лимит FPS, чтобы он не сбрасывался на рекомендуемый при
    /// перезапуске приложения. Пустое поле = вернуться к рекомендации.</summary>
    private void TxtFpsCap_LostFocus(object sender, RoutedEventArgs e)
    {
        var t = TxtFpsCap.Text?.Trim() ?? "";
        if (int.TryParse(t, out var v) && v >= 0) Ctx().Backup.SaveState("fps-cap", v.ToString());
        else if (t.Length == 0) Ctx().Backup.SaveState("fps-cap", "");   // очистили — снова рекомендация
    }

    private void CopyLaunch_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(TxtLaunch.Text); BtnCopyLaunch.Content = Loc.T("cs2.copied"); }
        catch { /* clipboard busy */ }
    }

    /// <summary>Разовый сброс кэша шейдеров CS2/GPU (лечит фризы рекомпиляции). Кэш пересоберётся.</summary>
    private async void ShaderClean_Click(object sender, RoutedEventArgs e)
    {
        BtnShaderClean.IsEnabled = false;
        TxtShaderStatus.Visibility = Visibility.Visible;
        TxtShaderStatus.Text = Loc.T("cs2.shader.cleaning");
        try
        {
            await Task.Run(() => Core.ShaderCacheTweak.CleanNow(s => Core.Logger.Info(s)));
            TxtShaderStatus.Text = Loc.T("cs2.shader.done");
        }
        catch (Exception ex) { await ShowErrorDialog("shader-clean", ex); }
        finally { BtnShaderClean.IsEnabled = true; }
    }

    private async void InstallAutoexec_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int? cap = int.TryParse(TxtFpsCap.Text?.Trim(), out var c) && c >= 0 ? c : null;
            if (cap is int v) Ctx().Backup.SaveState("fps-cap", v.ToString()); // запомнить свой лимит
            var path = Cs2Config.InstallAutoexec(_hw, cap);
            RefreshCs2();
            await ShowInfoDialog(Loc.T("cs2.title"), string.Format(Loc.T("cs2.install.ok"), path));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog("install-autoexec", ex);
        }
    }

    /// <summary>Обновить блок игрового конфига графики (наличие файла, доступность кнопок, статус).</summary>
    private void RefreshVideoConfig()
    {
        if (BtnApplyVideo is null) return;
        var path = Cs2VideoConfig.ConfigPath();
        BtnApplyVideo.IsEnabled = path is not null;
        BtnRestoreVideo.IsEnabled = Cs2VideoConfig.HasBackup();
        if (path is null)
        {
            TxtVideoStatus.Visibility = Visibility.Visible;
            TxtVideoStatus.Text = Loc.T("cs2.video.nofile");
        }
        else if (Cs2VideoConfig.IsApplied())
        {
            TxtVideoStatus.Visibility = Visibility.Visible;
            TxtVideoStatus.Text = Loc.T("cs2.video.applied");
        }
        else TxtVideoStatus.Visibility = Visibility.Collapsed;
    }

    private async void ApplyVideo_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = Cs2VideoConfig.ApplyMaxFps();
        RefreshVideoConfig();
        await ShowInfoDialog(Loc.T("cs2.title"), ok
            ? Loc.T("cs2.video.ok")
            : Loc.T("cs2.video.err." + msg switch
            {
                "cs2-running" => "running",
                "no-config" => "nofile",
                _ => "generic"
            }));
    }

    private async void RestoreVideo_Click(object sender, RoutedEventArgs e)
    {
        var (ok, msg) = Cs2VideoConfig.RestoreOriginal();
        RefreshVideoConfig();
        await ShowInfoDialog(Loc.T("cs2.title"), ok
            ? Loc.T("cs2.video.restored")
            : Loc.T("cs2.video.err." + (msg == "cs2-running" ? "running" : "generic")));
    }

    // ---------- Экран мониторинга ----------
    // Ручной замер по PresentMon убран: замер теперь полностью автоматический — по воркшоп-карте
    // бенчмарка (детерминированный сценарий), см. AutoBench_Click.

    private string BenchMapId()
    {
        var saved = Ctx().Backup.LoadState("bench-map-id")?.Trim();
        return Core.Cs2Benchmark.IsValidId(saved) ? saved! : Core.Cs2Benchmark.DefaultWorkshopMapId;
    }

    private void BenchMapId_LostFocus(object sender, RoutedEventArgs e)
    {
        var id = TxtBenchMapId.Text?.Trim() ?? "";
        if (Core.Cs2Benchmark.IsValidId(id)) Ctx().Backup.SaveState("bench-map-id", id);
    }

    private void SubscribeMap_Click(object sender, RoutedEventArgs e)
        => Core.Cs2Benchmark.OpenWorkshopPage(TxtBenchMapId.Text?.Trim() ?? BenchMapId());

    /// <summary>Полу-авто замер по ЛЮБОЙ карте-бенчмарку. Прога запускает CS2 с -condebug (пишет
    /// console.log) и +fps_max 0 (снять кап на сессию), а КАРТУ пользователь выбирает сам в игре
    /// (Играть → Мастерская → карта → Go). Причина ручного шага: CS2 игнорирует host_workshop_map
    /// как стартовую команду, а подать команду извне нельзя (netcon удалён, VConsole = риск VAC).
    /// Мы ловим итог ЛЮБОЙ карты из консоли (строка [VProf] FPS: Avg=..., P1=...) и показываем A/B.</summary>
    private async void AutoBench_Click(object sender, RoutedEventArgs e)
    {
        // Второй клик во время слежения = остановить.
        if (_benchCts is not null) { _benchCts.Cancel(); return; }
        if (_busy) return;

        // Бенчмарк «до/после» — функция Pro.
        if (!Core.LicenseService.IsPro)
        {
            await ShowInfoDialog(Loc.T("pro.title"), Loc.T("pro.benchmark"));
            return;
        }

        // CS2 должен быть закрыт — мы запускаем его сами с -condebug (иначе console.log не пишется).
        if (PresentMonService.IsCs2Running())
        {
            await ShowInfoDialog(Loc.T("monitor.auto.title"), Loc.T("monitor.auto.running"));
            return;
        }

        Core.Cs2Benchmark.ClearConsoleLog();               // чтобы не поймать прошлый VProf-итог
        if (!Core.Cs2Benchmark.LaunchForBenchmark())
        {
            await ShowInfoDialog(Loc.T("monitor.auto.title"), Loc.T("monitor.auto.launchfail"));
            return;
        }

        _busy = true;
        var origBtn = TxtAutoBench.Text;
        // Ждём старт процесса CS2.
        for (int i = 0; i < 90 && !PresentMonService.IsCs2Running(); i++)
        {
            TxtMonResult.Text = Loc.T("monitor.auto.launching");
            await Task.Delay(1000);
        }
        if (!PresentMonService.IsCs2Running())
        {
            _busy = false;
            TxtMonResult.Text = "⚠ " + Loc.T("monitor.auto.timeout");
            return;
        }

        // Непрерывный захват PresentMon (best-effort): график фреймтайма + реальные статтер/StdDev/0.1%,
        // которых карта-бенчмарк не отдаёт. Если PresentMon недоступен — просто без графика.
        var (pmProc, pmCsv) = PresentMonService.StartContinuousCapture();
        _benchCts = new System.Threading.CancellationTokenSource();
        var tok = _benchCts.Token;
        TxtAutoBench.Text = Loc.T("monitor.auto.stop");   // кнопка становится «Остановить слежение»

        int seen = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // НЕПРЕРЫВНОЕ слежение: ловим КАЖДЫЙ новый итог карты, пока CS2 открыта (и не дольше 40 мин).
            // Пользователь может применить твик и снова запустить карту, НЕ закрывая игру — подхватим.
            while (!tok.IsCancellationRequested && PresentMonService.IsCs2Running()
                   && sw.Elapsed < TimeSpan.FromMinutes(40))
            {
                var results = Core.Cs2Benchmark.ParseVProfResults();
                if (results.Count > seen)
                {
                    var r = results[^1];
                    seen = results.Count;

                    FrametimeStats? pm = null; double[] series = Array.Empty<double>();
                    if (pmCsv is not null) { var pr = PresentMonService.ParseRecent(pmCsv); pm = pr.stats; series = pr.series; }

                    // Заголовочные Avg/P1 — из карты (детерминированный бенчмарк). Статтер/StdDev/0.1%
                    // low — из PresentMon (карта их не даёт). Без PresentMon они = 0 (как раньше).
                    var stats = new FrametimeStats(
                        pm?.Frames ?? 0, Math.Round(r.avg, 1), Math.Round(r.p1, 1),
                        pm?.Low01Fps ?? 0, pm?.MaxStutterMs ?? 0, pm?.StdDevMs ?? 0);
                    _lastStats = stats;

                    TxtMonResult.Text = string.Format(Loc.T("monitor.auto.result"), stats.AvgFps, stats.Low1Fps)
                        + (seen > 1 ? "   ·  " + string.Format(Loc.T("monitor.auto.run"), seen) : "");
                    BtnSetBaseline.IsEnabled = true;
                    ShowDeltaVsBaseline(stats);
                    DrawFrametime(series);
                    Ctx().Backup.SaveState("last-avg-fps", stats.AvgFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Ctx().Backup.SaveState("last-low1-fps", stats.Low1Fps.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _ = RefreshForecast();
                    BuildDashboard();
                }
                else
                {
                    TxtMonResult.Text = seen == 0
                        ? string.Format(Loc.T("monitor.auto.waiting"), (int)sw.Elapsed.TotalSeconds)
                        : string.Format(Loc.T("monitor.auto.watching"), seen);
                }
                try { await Task.Delay(2500, tok); } catch (TaskCanceledException) { }
            }
            if (seen == 0 && !tok.IsCancellationRequested)
                TxtMonResult.Text = "⚠ " + Loc.T("monitor.auto.timeout");
        }
        finally
        {
            try { if (pmProc is { HasExited: false }) pmProc.Kill(true); } catch { }
            pmProc?.Dispose();
            _benchCts.Dispose(); _benchCts = null;
            _busy = false;
            TxtAutoBench.Text = origBtn;
            RefreshCs2();
        }
    }

    /// <summary>Нарисовать фреймтайм последнего прогона (ряд мс между кадрами) в графике монитора.
    /// Шкала 0..20 мс: ровная линия внизу = плавно, пики вверх = микрофризы.</summary>
    private void DrawFrametime(double[] series)
    {
        if (GraphLine is null || GraphFill is null || CardMonGraph is null) return;
        if (series is null || series.Length < 2) { CardMonGraph.Visibility = Visibility.Collapsed; return; }
        const double W = 600, H = 120, maxMs = 20.0;
        int n = series.Length;
        var line = new System.Windows.Media.PointCollection(n);
        var fill = new System.Windows.Media.PointCollection(n + 2) { new System.Windows.Point(0, H) };
        for (int i = 0; i < n; i++)
        {
            double x = W * i / (n - 1);
            double ms = Math.Min(series[i], maxMs);
            double y = H * (1 - ms / maxMs);   // высокий фреймтайм (плохо) = пик вверх
            line.Add(new System.Windows.Point(x, y));
            fill.Add(new System.Windows.Point(x, y));
        }
        fill.Add(new System.Windows.Point(W, H));
        GraphLine.Points = line;
        GraphFill.Points = fill;
        CardMonGraph.Visibility = Visibility.Visible;
    }

    // ---------- A/B: база «до» и дельта «после» ----------
    private const string BaselineKey = "bench-baseline";

    /// <summary>Запомнить последний замер как эталон «до» (перед применением твика).</summary>
    private void SetBaseline_Click(object sender, RoutedEventArgs e)
    {
        if (_lastStats is null) return;
        Ctx().Backup.SaveState(BaselineKey,
            System.Text.Json.JsonSerializer.Serialize(_lastStats));
        RefreshBaselineLabel();
        CardMonDelta.Visibility = Visibility.Collapsed;   // старая дельта уже неактуальна
    }

    private FrametimeStats? LoadBaseline()
    {
        var json = Ctx().Backup.LoadState(BaselineKey);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<FrametimeStats>(json); }
        catch { return null; }
    }

    /// <summary>Подпись «эталон до: …» под результатом замера.</summary>
    private void RefreshBaselineLabel()
    {
        if (TxtMonBaseline is null) return;
        var b = LoadBaseline();
        if (b is null) { TxtMonBaseline.Text = Loc.T("monitor.baseline.none"); return; }
        // Показываем только метрики, которые реально есть в замере. Замер карты-бенчмарка даёт лишь
        // FPS + 1% low, остальное = 0 — эти строки не выводим, чтобы не висело «статтер 0 · ровность 0».
        var parts = new List<string> { $"FPS {b.AvgFps}", $"1% low {b.Low1Fps}" };
        if (b.Low01Fps > 0) parts.Add($"0.1% low {b.Low01Fps}");
        if (b.MaxStutterMs > 0) parts.Add($"статтер {b.MaxStutterMs} мс");
        if (b.StdDevMs > 0) parts.Add($"ровность {b.StdDevMs} мс");
        TxtMonBaseline.Text = string.Format(Loc.T("monitor.baseline.cur"), string.Join(" · ", parts));
    }

    /// <summary>Показать дельту свежего замера относительно эталона «до».</summary>
    private void ShowDeltaVsBaseline(FrametimeStats now)
    {
        var b = LoadBaseline();
        if (b is null) { CardMonDelta.Visibility = Visibility.Collapsed; return; }
        // Для FPS и low «больше = лучше» (up=true); для статтера и StdDev «меньше = лучше» (up=false).
        // Метрику показываем только если она есть в ОБОИХ замерах (замер карты даёт лишь FPS+1% low —
        // остальное 0, эти строки пропускаем, чтобы не показывать бессмысленные нули).
        var parts = new List<string> {
            D("FPS", now.AvgFps - b.AvgFps, up: true),
            D("1% low", now.Low1Fps - b.Low1Fps, up: true),
        };
        if (now.Low01Fps > 0 && b.Low01Fps > 0) parts.Add(D("0.1% low", now.Low01Fps - b.Low01Fps, up: true));
        if (now.MaxStutterMs > 0 && b.MaxStutterMs > 0) parts.Add(D("статтер", now.MaxStutterMs - b.MaxStutterMs, up: false, unit: " мс"));
        if (now.StdDevMs > 0 && b.StdDevMs > 0) parts.Add(D("ровность", now.StdDevMs - b.StdDevMs, up: false, unit: " мс"));
        TxtMonDelta.Text = string.Join("   ", parts);
        CardMonDelta.Visibility = Visibility.Visible;

        static string D(string name, double delta, bool up, string unit = "")
        {
            var d = Math.Round(delta, 2);
            bool better = up ? d > 0 : d < 0;
            var arrow = d == 0 ? "=" : (better ? "▲" : "▼");
            var sign = d > 0 ? "+" : "";
            return $"{name} {arrow}{sign}{d.ToString(System.Globalization.CultureInfo.InvariantCulture)}{unit}";
        }
    }

    // ---------- Тест: оценка + прогноз FPS ----------

    private async Task RefreshForecast()
    {
        // Состояние A/B-базы при каждом заходе на панель.
        if (BtnSetBaseline is not null) BtnSetBaseline.IsEnabled = _lastStats is not null;
        RefreshBaselineLabel();
        if (TxtBenchMapId is not null && string.IsNullOrWhiteSpace(TxtBenchMapId.Text))
            TxtBenchMapId.Text = BenchMapId();

        // Мгновенно показываем прошлый результат из кэша (OptimizationScore опрашивает IsApplied
        // всех твиков — часть спавнит powercfg/PowerShell, потому без кэша панель висела с «—»).
        // Реальный расчёт уходит в фон (VM) и перезаписывает кэш.
        var cached = _vm.LoadForecastSnapshot();
        if (cached is not null) ShowForecast(cached);
        else { TxtScore.Text = "…"; TxtForecast.Text = Loc.T("loading"); TxtForecastSub.Text = ""; }

        ShowForecast(await _vm.ComputeForecastAsync());
    }

    private void ShowForecast(ForecastSnapshot? s)
    {
        if (s is null) return;
        TxtScore.Text = s.Score.ToString();
        TxtScoreSub.Text = string.Format(Loc.T("score.sub"), s.Applied, s.Supported);
        TxtForecast.Text = string.Format(Loc.T("forecast.line"), s.Cur, s.Soft, s.Bios);
        TxtForecastSub.Text = s.FromMeas ? Loc.T("forecast.measured") : Loc.T("forecast.estimate");
    }

    // ---------- Экран BIOS ----------
    // Список привязан к _vm.BiosItems (ItemsControl + DataTemplate в XAML).
    private void BuildBios()
    {
        var hw = _hw ?? HardwareInfo.Detect();
        _vm.BiosItems.Clear();
        foreach (var tip in BiosAdvisor.For(hw))
            _vm.BiosItems.Add(AdviceCardViewModel.FromLevel(tip.Level, tip.Title, tip.Body, Loc.Lang));
    }

    // ---------- Экран «Откат» ----------
    // Список привязан к _vm.RestoreItems (ItemsControl + DataTemplate). Кэш-первая отрисовка,
    // спиннер, фоновая сверка и покарточный откат (RevertCommand) живут в MainViewModel.
    private async void BuildRestore() => await _vm.BuildRestoreAsync(Loc.Lang);

    private async void RestoreRevertAll_Click(object sender, RoutedEventArgs e)
        => await _vm.RevertAllRestoreAsync(Loc.Lang);

    private void RestoreRescan_Click(object sender, RoutedEventArgs e) => BuildRestore();

    // ---------- Локализация текста ----------
    private void ApplyTexts()
    {
        TxtBrandSub.Text = Loc.T("brand.sub");
        TxtNavDash.Text = Loc.T("nav.dash");
        TxtNavBoost.Text = Loc.T("nav.boost");
        TxtNavTweaks.Text = Loc.T("nav.tweaks");
        TxtNavMonitor.Text = Loc.T("nav.monitor");
        TxtNavLab.Text = Loc.T("nav.lab");
        TxtNavCs2.Text = Loc.T("nav.cs2");
        TxtNavRestore.Text = Loc.T("nav.restore");

        TxtTitle.Text = Loc.T("dash.title");
        TxtTagline.Text = Loc.T("dash.tagline");
        TxtFindingsHdr.Text = Loc.T("findings.hdr");
        TxtBoostModeTitle.Text = Loc.T("boostmode.title");
        TxtBoostModeDesc.Text = Loc.T("boostmode.desc");
        TxtNavSettings.Text = Loc.T("nav.settings");
        TxtSettingsTitle.Text = Loc.T("settings.title");
        TxtSettingsSub.Text = Loc.T("settings.sub");
        // Имена для скринридера: тумблеры сами по себе безымянные (заголовок лежит рядом
        // отдельным TextBlock), без этого озвучиваются как безликий «переключатель».
        System.Windows.Automation.AutomationProperties.SetName(BoostToggle, Loc.T("boostmode.title"));
        System.Windows.Automation.AutomationProperties.SetName(AutoStartToggle, Loc.T("auto.start.title"));
        System.Windows.Automation.AutomationProperties.SetName(AutoGameToggle, Loc.T("auto.game.title"));
        TxtAutoStartTitle.Text = Loc.T("auto.start.title");
        TxtAutoStartDesc.Text = Loc.T("auto.start.desc");
        TxtAutoGameTitle.Text = Loc.T("auto.game.title");
        TxtAutoGameDesc.Text = Loc.T("auto.game.desc");
        TxtProTierLabel.Text = Loc.T("pro.tier") + ":";
        TxtProPromoHint.Text = Loc.T("pro.promo.hint");
        BtnPromo.Content = Loc.T("pro.promo.btn");
        RefreshPro();
        TxtUpdatesTitle.Text = Loc.T("update.title");
        TxtUpdateVersion.Text = $"{Loc.T("update.version")} {Core.UpdateService.CurrentVersion.ToString(3)}";
        TxtUpdateStatus.Text = Loc.T("update.hint");
        BtnCheckUpdate.Content = Loc.T("update.check");
        TxtWelcomeSub.Text = Loc.T("welcome.sub");
        TxtWelcomeScan.Text = Loc.T("welcome.scan");
        TxtOptimize.Text = Loc.T("dash.optimize");
        BtnScan.Content = Loc.T("dash.scan");
        BtnTheme.Content = Loc.T("theme");
        LblCpu.Text = Loc.T("spec.cpu");
        LblGpu.Text = Loc.T("spec.gpu");
        LblRam.Text = Loc.T("spec.ram");
        LblMon.Text = Loc.T("spec.mon");

        TxtTweaksTitle.Text = Loc.T("tweaks.title");
        TxtTweaksSub.Text = Loc.T("tweaks.sub");
        TxtApplySelected.Text = Loc.T("tweaks.apply");
        BtnRevertAll.Content = Loc.T("tweaks.revertall");
        TxtLabTitle.Text = Loc.T("lab.title");
        TxtLabSub.Text = Loc.T("lab.sub");
        TxtLabBenchTitle.Text = Loc.T("lab.bench.title");
        TxtLabBenchBody.Text = Loc.T("lab.bench.body");
        BtnLabToMonitor.Content = Loc.T("lab.tomonitor");
        TxtLabEmpty.Text = Loc.T("lab.empty");
        TxtApplyLab.Text = Loc.T("tweaks.apply");
        TxtBoostTitle.Text = Loc.T("boost.title");
        TxtBoostSub.Text = Loc.T("boost.sub");
        TxtProfSafe.Text = Loc.T("profile.safe");
        TxtProfOptimal.Text = Loc.T("profile.optimal");
        TxtProfMax.Text = Loc.T("profile.max");
        TxtProfRecommended.Text = Loc.T("profile.recommended");
        TxtProfSafeDesc.Text = ProfileDesc("profile.safe.desc", Profile.Safe);
        TxtProfOptimalDesc.Text = ProfileDesc("profile.optimal.desc", Profile.Optimal);
        TxtProfMaxDesc.Text = ProfileDesc("profile.max.desc", Profile.Maximum);
        RefreshBoostHighlight();  // подписи кнопок планов («Применить»/«Активно») + подсветка
        TxtMonTitle.Text = Loc.T("test.title");
        TxtMonSub.Text = Loc.T("test.sub");
        TxtScoreTitle.Text = Loc.T("score.title");
        TxtForecastTitle.Text = Loc.T("forecast.title");
        TxtMonHint.Text = Loc.T("monitor.hint");
        TxtAutoBench.Text = Loc.T("monitor.auto.btn");
        BtnSetBaseline.Content = Loc.T("monitor.baseline.set");
        TxtAutoBenchHint.Text = Loc.T("monitor.auto.hint");
        LblBenchMap.Text = Loc.T("monitor.benchmap.label");
        BtnSubscribeMap.Content = Loc.T("monitor.subscribe");
        TxtMonGraphTitle.Text = Loc.T("monitor.graph.title");
        TxtMonGraphCap.Text = Loc.T("monitor.graph.cap");
        TxtMonDeltaTitle.Text = Loc.T("monitor.delta.title");
        RefreshBaselineLabel();
        TxtNavBios.Text = Loc.T("nav.bios");
        TxtBiosTitle.Text = Loc.T("bios.title");
        TxtBiosSub.Text = Loc.T("bios.sub");
        TxtCs2Title.Text = Loc.T("cs2.title");
        TxtCs2Sub.Text = Loc.T("cs2.sub");
        LblCs2Path.Text = Loc.T("cs2.path");
        LblLaunch.Text = Loc.T("cs2.launch");
        BtnCopyLaunch.Content = Loc.T("cs2.copy");
        TxtCs2Instr.Text = Loc.T("cs2.instr");
        LblAutoexec.Text = Loc.T("cs2.autoexec");
        LblFpsCap.Text = Loc.T("cs2.fps.label");
        TxtInstallAutoexec.Text = Loc.T("cs2.install");
        TxtVideoTitle.Text = Loc.T("cs2.video.title");
        TxtVideoDesc.Text = Loc.T("cs2.video.desc");
        TxtVideoWarn.Text = Loc.T("cs2.video.warn");
        TxtApplyVideo.Text = Loc.T("cs2.video.apply");
        BtnRestoreVideo.Content = Loc.T("cs2.video.restore");
        TxtShaderTitle.Text = Loc.T("cs2.shader.title");
        TxtShaderDesc.Text = Loc.T("cs2.shader.desc");
        BtnShaderClean.Content = Loc.T("cs2.shader.btn");
        RefreshCs2();
        TxtRestTitle.Text = Loc.T("restore.title");
        TxtRestSub.Text = Loc.T("restore.sub");
        TxtRestEmpty.Text = Loc.T("restore.empty");
        TxtRestRevertAll.Text = Loc.T("tweaks.revertall");
        BtnRestoreRescan.Content = Loc.T("restore.refresh");
        TxtBackupNote.Text = Loc.T("restore.note");

        if (_hw is null) return;

        // В карточке — короткое имя (сырое из WMI не влезает), полное остаётся в подсказке.
        ValCpu.Text = _hw.CpuShort; ValCpu.ToolTip = _hw.CpuName;
        NoteCpu.Text = $"{_hw.CpuCores}c/{_hw.CpuThreads}t · {Loc.T("note.cpu.limit")}";
        ValGpu.Text = _hw.GpuShort; ValGpu.ToolTip = _hw.GpuName;
        NoteGpu.Text = _hw.HasDiscreteGpu ? Loc.T("note.gpu.ok") : Loc.T("note.gpu.igpu");
        // Канальность ушла из значения в подпись: строка «20 GB · 1867 MHz · двухканал»
        // не влезала в четверть ширины и обрезалась.
        ValRam.Text = $"{_hw.RamGb} GB · {_hw.RamSpeedMhz} MHz";
        var chan = _hw.RamModules > 0 ? $"{(_hw.RamSingleChannel ? Loc.T("ram.single") : Loc.T("ram.dual"))} · " : "";
        NoteRam.Text = chan + (_hw.RamSingleChannel ? Loc.T("note.ram.single")
            : _hw.RamMixedKit ? Loc.T("note.ram.mixed") : Loc.T("note.ram.ok"));
        ValMon.Text = _hw.MonitorCount > 1 ? $"{_hw.MonitorHz} Hz · ×{_hw.MonitorCount}" : $"{_hw.MonitorHz} Hz";
        // Подпись — оценка САМОГО монитора (раньше сюда попадала версия Windows — не по теме карточки).
        NoteMon.Text = _hw.MonitorHz >= 144 ? Loc.T("note.mon.high")
            : _hw.MonitorHz >= 100 ? Loc.T("note.mon.mid")
            : _hw.MonitorHz > 0 ? Loc.T("note.mon.low") : "—";

        // Адаптивный «Диагноз» + отчёт строит BuildDashboard (по вердикту узкого места).
        BuildDashboard();
    }

    // ---------- Экран «Твики» из каталога ----------
    // Список привязан к _vm.Tweaks (ItemsControl + DataTemplate в XAML). Мгновенная отрисовка
    // из кэша + фоновая сверка реального состояния живут в MainViewModel.BuildTweaksAsync.
    private async void BuildTweaks() { await _vm.BuildTweaksAsync(Loc.Lang); RefreshTweaksUpsell(); }

    /// <summary>Открыть раздел «Настройки» (там активация Pro/промокод).</summary>
    private void GoPro_Click(object sender, RoutedEventArgs e) => ShowPanel("settings");

    /// <summary>Показать/скрыть апселл под списком твиков (в Free, если что-то скрыто за Pro).</summary>
    private void RefreshTweaksUpsell()
    {
        if (CardTweaksUpsell is null) return;
        bool show = !_vm.IsProTier && _vm.LockedCount > 0;
        CardTweaksUpsell.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) TxtTweaksUpsell.Text = string.Format(Loc.T("pro.locked"), _vm.LockedCount);
    }

    // ---------- Dev-самоскриншот (--shot=<путь>) ----------
    private void MaybeSelfShot()
    {
        var args = Environment.GetCommandLineArgs();
        var shotArg = args.FirstOrDefault(a => a.StartsWith("--shot=", StringComparison.OrdinalIgnoreCase));
        if (shotArg is null) return;
        var path = shotArg["--shot=".Length..].Trim('"');

        var panelArg = args.FirstOrDefault(a => a.StartsWith("--panel=", StringComparison.OrdinalIgnoreCase));
        if (panelArg is not null)
        {
            var id = panelArg["--panel=".Length..].Trim('"');
            if (id == "loading") PanelLoading.Visibility = Visibility.Visible;   // dev: снять экран загрузки
            else ShowPanel(id);
        }

        if (args.Any(a => a.Equals("--light", StringComparison.OrdinalIgnoreCase))) ApplyPalette(false);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try { CaptureSelf(path); } catch { /* ignore */ }
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    private void CaptureSelf(string path)
    {
        int w = (int)Math.Ceiling(ActualWidth);
        int h = (int)Math.Ceiling(ActualHeight);
        if (w <= 0 || h <= 0) return;
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        var back = _dark ? Color.FromRgb(0x14, 0x11, 0x0D) : Color.FromRgb(0xF7, 0xF1, 0xE7);
        using (var dc = backdrop.RenderOpen())
            dc.DrawRectangle(new SolidColorBrush(back), null, new Rect(0, 0, w, h));
        rtb.Render(backdrop);
        rtb.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
