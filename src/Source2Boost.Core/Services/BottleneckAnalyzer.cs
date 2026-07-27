namespace Source2Boost.Core;

/// <summary>Основное узкое место системы для CS2.</summary>
public enum Bottleneck { Cpu, Gpu, Ram, Storage, Balanced, Unknown }

/// <summary>Пункт отчёта: слабое место / рекомендация под конкретный ПК.</summary>
public sealed record Finding(BiosTipLevel Level, L10n Title, L10n Detail);

/// <summary>Вердикт по системе: главное узкое место + человекочитаемый отчёт.</summary>
public sealed record SystemVerdict(
    Bottleneck Primary,
    L10n Headline,
    L10n Summary,
    IReadOnlyList<Finding> Findings);

/// <summary>
/// Определяет главное узкое место и собирает отчёт под конкретное железо (и опц. замер).
/// Основа адаптивности: из вердикта строится «Диагноз» на дашборде, отчёт первого запуска и
/// подсветка релевантных твиков. CS2 почти всегда CPU-bound — но проверяем память/накопитель/GPU.
/// </summary>
public static class BottleneckAnalyzer
{
    public static SystemVerdict Analyze(HardwareInfo hw, FrametimeStats? stats = null)
    {
        var findings = new List<Finding>();

        // ---- Runtime-вердикт по GPU-busy, если есть замер ----
        Bottleneck primary = Bottleneck.Unknown;
        var frac = stats?.GpuBusyFraction;
        if (frac is double f)
            primary = f >= 0.90 ? Bottleneck.Gpu : f <= 0.70 ? Bottleneck.Cpu : Bottleneck.Balanced;

        // ---- Память: ОДНА находка ----
        // Раньше «Разнокалиберная память» и «Включи XMP/DOCP» показывались рядом и обе говорили
        // про XMP — пользователь читал один и тот же совет дважды. Теперь собираем один текст:
        // главная проблема набора (одноканал / разные планки) + строка про XMP, если уместна.
        L10n? ramTitle = null;
        var ramLevel = BiosTipLevel.Recommended;
        var ru = new List<string>(); var uk = new List<string>(); var en = new List<string>();

        if (hw.RamSingleChannel)
        {
            ramLevel = BiosTipLevel.Warning;
            ramTitle = new L10n("Память в одноканале", "Пам'ять в одноканалі", "Single-channel RAM");
            ru.Add("Стоит одна планка — CS2 очень чувствителен к памяти. Вторая одинаковая планка (двухканал) даёт +20–40% FPS и убирает часть статтера. Самый крупный апгрейд за копейки.");
            uk.Add("Стоїть одна планка — CS2 дуже чутливий до пам'яті. Друга однакова планка (двоканал) дає +20–40% FPS і прибирає частину статтеру.");
            en.Add("One stick installed — CS2 is very memory-sensitive. A second identical stick (dual-channel) gives +20–40% FPS and removes some stutter. Biggest cheap upgrade.");
        }
        else if (hw.RamMixedKit)
        {
            ramLevel = BiosTipLevel.Warning;
            ramTitle = new L10n("Разнокалиберная память", "Різнокаліберна пам'ять", "Mismatched RAM");
            ru.Add($"Планки разные — такой набор работает на скорости самой медленной ({hw.RamSpeedMhz} МГц) и хуже держит разгон. Ровнее будет одинаковый комплект 2×.");
            uk.Add($"Планки різні — такий набір працює на швидкості найповільнішої ({hw.RamSpeedMhz} МГц) і гірше тримає розгін. Рівніше буде однаковий комплект 2×.");
            en.Add($"The sticks differ — such a kit runs at the slowest one's speed ({hw.RamSpeedMhz} MHz) and holds overclocks worse. A matched 2× kit is steadier.");
        }

        if (hw.RamRunningBelowRated)
        {
            // Явный сигнал: рабочая частота ниже паспортной → XMP/DOCP точно не включён.
            ramLevel = BiosTipLevel.Warning;
            ramTitle ??= new L10n("XMP выключен — память недоразогнана", "XMP вимкнено — пам'ять недорозігнана", "XMP off — RAM underclocked");
            ru.Add($"Сейчас память идёт на {hw.RamSpeedMhz} МГц при паспортных {hw.RamRatedMhz} МГц — профиль XMP/DOCP в BIOS выключен. Включи его: это бесплатные +8–15% FPS.");
            uk.Add($"Зараз пам'ять іде на {hw.RamSpeedMhz} МГц при паспортних {hw.RamRatedMhz} МГц — профіль XMP/DOCP у BIOS вимкнено. Увімкни його: це безкоштовні +8–15% FPS.");
            en.Add($"RAM runs at {hw.RamSpeedMhz} MHz but is rated {hw.RamRatedMhz} MHz — the XMP/DOCP profile is off in BIOS. Turn it on for a free +8–15% FPS.");
        }
        else if (hw.RamSpeedMhz is > 0 and <= 2400)
        {
            ramTitle ??= new L10n("Включи XMP/DOCP", "Увімкни XMP/DOCP", "Enable XMP/DOCP");
            ru.Add($"Частота {hw.RamSpeedMhz} МГц похожа на базовую — проверь, включён ли профиль XMP/DOCP в BIOS (это +8–12% FPS).");
            uk.Add($"Частота {hw.RamSpeedMhz} МГц схожа на базову — перевір, чи увімкнено профіль XMP/DOCP у BIOS (це +8–12% FPS).");
            en.Add($"{hw.RamSpeedMhz} MHz looks like base speed — check whether the XMP/DOCP profile is enabled in BIOS (+8–12% FPS).");
        }

        if (ramTitle is not null)
            findings.Add(new Finding(ramLevel, ramTitle,
                new L10n(string.Join(" ", ru), string.Join(" ", uk), string.Join(" ", en))));
        if (hw.RamGb is > 0 and <= 8)
            findings.Add(new Finding(BiosTipLevel.Optional,
                new L10n("8 ГБ RAM — впритык", "8 ГБ RAM — впритул", "8 GB RAM — tight"),
                new L10n("CS2 + Windows впритык укладываются в 8 ГБ. Наши твики памяти (standby, svchost, фон) помогут, но 16 ГБ заметно ровнее.",
                         "CS2 + Windows впритул вкладаються у 8 ГБ. Наші твіки пам'яті допоможуть, але 16 ГБ помітно рівніше.",
                         "CS2 + Windows barely fit in 8 GB. Our memory tweaks help, but 16 GB is noticeably steadier.")));

        // ---- Накопитель ----
        if (hw.Cs2OnSsd == false)
            findings.Add(new Finding(BiosTipLevel.Warning,
                new L10n("CS2 на жёстком диске (HDD)", "CS2 на жорсткому диску (HDD)", "CS2 on a hard drive (HDD)"),
                new L10n("Игра стоит на HDD — это добавляет 50–200 мс к загрузке шейдеров и усиливает статтер. Перенос на любой SSD (даже SATA) — огромный скачок плавности.",
                         "Гра на HDD — це додає 50–200 мс до завантаження шейдерів і посилює статтер. Перенесення на будь-який SSD — величезний стрибок плавності.",
                         "The game is on an HDD — adds 50–200 ms to shader loads and worsens stutter. Moving to any SSD (even SATA) is a huge smoothness jump.")));

        // ---- GPU ----
        if (!hw.HasDiscreteGpu)
        {
            findings.Add(new Finding(BiosTipLevel.Optional,
                new L10n("Только встроенная графика", "Лише вбудована графіка", "Integrated graphics only"),
                new L10n("Нет дискретной видеокарты — потолок FPS ограничен встройкой. Снизь разрешение/настройки графики в CS2 и обязательно включи XMP (встройка ест системную память).",
                         "Немає дискретної відеокарти — стеля FPS обмежена вбудованою. Знизь роздільність/налаштування у CS2 і обов'язково увімкни XMP.",
                         "No discrete GPU — FPS is capped by the iGPU. Lower CS2 resolution/settings and definitely enable XMP (the iGPU uses system RAM).")));
            if (primary == Bottleneck.Unknown) primary = Bottleneck.Gpu;
        }

        // ---- Гайд по панели драйвера GPU. NVIDIA НЕ показываем советом: всё, что там нужно
        // (питание, низкая задержка, verт. синхронизация, фильтрация текстур), делает твик
        // «Игровой профиль драйвера NVIDIA» через NVAPI, а Reflex ставит CS2-пресет. Даём совет
        // только там, где сами пока не можем — AMD (ADLX ещё не интегрирован). ----
        if (hw.IsAmd)
            findings.Add(new Finding(BiosTipLevel.Recommended,
                new L10n("Настрой AMD Adrenalin", "Налаштуй AMD Adrenalin", "Tune AMD Adrenalin"),
                new L10n("В AMD Software (Adrenalin) → Игры → CS2: Radeon Anti-Lag = Вкл, Radeon Chill = Выкл, Radeon Boost = Выкл, Режим сглаживания = Оверрайд/Выкл, Фильтрация текстур = Производительность, Ждать верт. обновления = Выкл. Это заметно снижает задержку и убирает провалы кадра.",
                         "У AMD Software (Adrenalin) → Ігри → CS2: Radeon Anti-Lag = Увімк, Radeon Chill = Вимк, Radeon Boost = Вимк, Фільтрація текстур = Продуктивність, Очікувати верт. оновлення = Вимк. Це помітно знижує затримку.",
                         "In AMD Software (Adrenalin) → Gaming → CS2: Radeon Anti-Lag = On, Radeon Chill = Off, Radeon Boost = Off, Texture Filtering = Performance, Wait for Vertical Refresh = Off. This noticeably cuts latency and frame dips.")));

        // ---- Безопасность (VBS) ----
        if (BiosAdvisor.MemoryIntegrityOn())
            findings.Add(new Finding(BiosTipLevel.Recommended,
                new L10n("Включена изоляция ядра (VBS)", "Увімкнена ізоляція ядра (VBS)", "Core Isolation (VBS) is on"),
                new L10n("«Целостность памяти» ест 5–25% CPU на слабом чипе. У нас есть твик её выключить (hvci-off) или сделай это в Безопасности Windows.",
                         "«Цілісність пам'яті» їсть 5–25% CPU на слабкому чипі. У нас є твік вимкнути (hvci-off) або зроби це в Безпеці Windows.",
                         "Memory Integrity costs 5–25% CPU on a weak chip. We have a tweak to disable it (hvci-off), or do it in Windows Security.")));

        // Сводки «честный потолок железа» здесь намеренно НЕТ: её рычаги строились из тех же
        // условий, что и находки выше (одноканал / разнокалиберная RAM / XMP / HDD), поэтому она
        // дословно их повторяла, а про упор в CPU и так говорит заголовок вердикта.

        // ---- Если вердикт всё ещё не определён — эвристика по железу (CS2 обычно CPU-bound) ----
        if (primary == Bottleneck.Unknown)
            primary = Bottleneck.Cpu;

        var (headline, summary) = Verdict(primary, hw);
        return new SystemVerdict(primary, headline, summary, findings);
    }

    private static (L10n headline, L10n summary) Verdict(Bottleneck b, HardwareInfo hw) => b switch
    {
        Bottleneck.Gpu => (
            new L10n("Узкое место: видеокарта", "Вузьке місце: відеокарта", "Bottleneck: GPU"),
            new L10n("GPU загружен под завязку. Помогут: макс. производительность GPU (наши твики), ниже разрешение и тяжёлые эффекты (тени, объёмный дым) в CS2.",
                     "GPU завантажений вщент. Допоможуть: макс. продуктивність GPU, нижча роздільність і важкі ефекти в CS2.",
                     "The GPU is maxed out. Fixes: max GPU performance (our tweaks), lower resolution and heavy effects (shadows, volumetric smoke) in CS2.")),
        Bottleneck.Ram => (
            new L10n("Узкое место: память", "Вузьке місце: пам'ять", "Bottleneck: memory"),
            new L10n("Упор в память. Двухканал + XMP + наши твики памяти — главный приоритет.",
                     "Упор у пам'ять. Двоканал + XMP + наші твіки пам'яті — головний пріоритет.",
                     "Memory-bound. Dual-channel + XMP + our memory tweaks are the priority.")),
        Bottleneck.Storage => (
            new L10n("Узкое место: накопитель", "Вузьке місце: накопичувач", "Bottleneck: storage"),
            new L10n("Медленный диск даёт статтер загрузки. Перенеси CS2 на SSD.",
                     "Повільний диск дає статтер завантаження. Перенеси CS2 на SSD.",
                     "Slow disk causes load stutter. Move CS2 to an SSD.")),
        Bottleneck.Balanced => (
            new L10n("Система сбалансирована", "Система збалансована", "System is balanced"),
            new L10n("CPU и GPU нагружены примерно поровну. Применяй Оптимальный профиль и следи за фреймтаймом.",
                     "CPU і GPU навантажені приблизно порівну. Застосовуй Оптимальний профіль.",
                     "CPU and GPU are loaded roughly evenly. Apply the Optimal profile and watch frametime.")),
        _ => (
            new L10n("Узкое место: процессор", "Вузьке місце: процесор", "Bottleneck: CPU"),
            new L10n("CS2 упирается в процессор (грузит 1–2 ядра) — это норма для движка. Наш агрессивный набор CPU-твиков + Spectre/VBS off дают максимум здесь.",
                     "CS2 впирається в процесор (вантажить 1–2 ядра) — норма для рушія. Наш агресивний набір CPU-твіків дає максимум тут.",
                     "CS2 is CPU-bound (loads 1–2 cores) — normal for the engine. Our aggressive CPU tweak set + Spectre/VBS off give the most here.")),
    };
}
