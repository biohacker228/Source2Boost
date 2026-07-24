using Microsoft.Win32;

namespace Source2Boost.Core;

/// <summary>Важность BIOS-рекомендации для сортировки/раскраски в UI.</summary>
public enum BiosTipLevel { Recommended, Optional, Warning }

/// <summary>Одна BIOS/UEFI-рекомендация: что сделать и почему, трёхъязычно.</summary>
public sealed record BiosTip(BiosTipLevel Level, L10n Title, L10n Body);

/// <summary>
/// Формирует список BIOS/UEFI-рекомендаций под конкретное железо. Часть пунктов
/// условна (появляются только если проблема детектится: XMP явно выключен, набор
/// планок разнокалиберный, включена виртуализация/VBS). BIOS мы не трогаем из
/// программы (это невозможно и небезопасно) — только объясняем пользователю.
/// </summary>
public static class BiosAdvisor
{
    /// <summary>Похоже ли, что RAM работает на базовой (JEDEC) частоте, а не на XMP-профиле.</summary>
    private static bool LikelyXmpOff(HardwareInfo hw)
    {
        if (hw.RamSpeedMhz <= 0) return false;
        // DDR3 XMP-киты обычно 1866–2133; DDR4 — 2666+; базовые JEDEC = 1333/1600/2133/2400.
        // Грубая эвристика: если планок ≥2 и частота на «типовой базе» — XMP скорее выключен.
        return hw.RamSpeedMhz <= 2400;
    }

    /// <summary>Включена ли изоляция ядра / VBS (HVCI) — заметно ест CPU, особенно на Skylake.</summary>
    public static bool MemoryIntegrityOn()
    {
        try
        {
            using var k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            // Enabled=1 означает, что Memory Integrity включён (работает через виртуализацию).
            return k?.GetValue("Enabled") is int v && v == 1;
        }
        catch { return false; }
    }

    public static IReadOnlyList<BiosTip> For(HardwareInfo hw)
    {
        var tips = new List<BiosTip>();

        // 1) XMP/DOCP — самый частый недокрут: RAM крутится на базовой частоте.
        if (hw.RamMixedKit)
        {
            tips.Add(new BiosTip(BiosTipLevel.Warning,
                new L10n("Разнокалиберный набор RAM",
                         "Різнокаліберний набір RAM",
                         "Mismatched RAM kit"),
                new L10n($"Планки памяти разные (обнаружено смешение объёмов/частот/производителей, сейчас {hw.RamSpeedMhz} МГц). Такой набор часто не держит XMP стабильно и работает на частоте самой медленной планки. Для CS2 память — реальное узкое место: по возможности поставь одинаковый двухканальный комплект (2×одинаковые) и включи XMP/DOCP.",
                         $"Планки пам'яті різні (виявлено суміш об'ємів/частот/виробників, зараз {hw.RamSpeedMhz} МГц). Такий набір часто не тримає XMP стабільно і працює на частоті найповільнішої планки. Для CS2 пам'ять — реальне вузьке місце: за можливості постав однаковий двоканальний комплект (2×однакові) та увімкни XMP/DOCP.",
                         $"Your RAM sticks differ (mixed capacities/speeds/vendors detected, currently {hw.RamSpeedMhz} MHz). Such a kit often won't hold XMP and runs at the slowest stick's speed. RAM is a real CS2 bottleneck: ideally fit a matched dual-channel kit (2× identical) and enable XMP/DOCP.")));
        }
        else if (LikelyXmpOff(hw))
        {
            tips.Add(new BiosTip(BiosTipLevel.Recommended,
                new L10n("Включи XMP / DOCP",
                         "Увімкни XMP / DOCP",
                         "Enable XMP / DOCP"),
                new L10n($"Память сейчас на {hw.RamSpeedMhz} МГц — похоже на базовую частоту без разгонного профиля. В BIOS/UEFI включи профиль XMP (Intel) или DOCP/EXPO (AMD): память заработает на своей паспортной частоте. Это один из самых заметных приростов FPS в CS2, т.к. игра упирается в память и CPU.",
                         $"Пам'ять зараз на {hw.RamSpeedMhz} МГц — схоже на базову частоту без розгінного профілю. У BIOS/UEFI увімкни профіль XMP (Intel) або DOCP/EXPO (AMD): пам'ять запрацює на паспортній частоті. Це один із найпомітніших приростів FPS у CS2, бо гра впирається в пам'ять та CPU.",
                         $"RAM is at {hw.RamSpeedMhz} MHz — looks like base speed with no overclock profile. In BIOS/UEFI enable the XMP (Intel) or DOCP/EXPO (AMD) profile so the RAM runs at its rated speed. It's one of the biggest CS2 FPS gains since the game is memory/CPU-bound.")));
        }

        // 2) Изоляция ядра (VBS) + виртуализация — ОДИН пункт: раньше это были два совета,
        // которые оба говорили про VBS (первый даже ссылался «см. ниже» на второй).
        // Само выключение VBS умеет твик hvci-off, поэтому здесь только то, что руками в BIOS.
        if (MemoryIntegrityOn())
        {
            tips.Add(new BiosTip(BiosTipLevel.Recommended,
                new L10n("Изоляция ядра включена — и виртуализация",
                         "Ізоляція ядра увімкнена — і віртуалізація",
                         "Core Isolation is on — and virtualization"),
                new L10n("«Изоляция ядра / Целостность памяти» (VBS) работает через виртуализацию и стабильно отъедает CPU — на старых чипах это заметно в CS2. Саму изоляцию выключает наш твик hvci-off. А в BIOS: если ты не пользуешься виртуальными машинами, WSL, Docker или песочницей Windows — можно дополнительно выключить виртуализацию (Intel VT-x / AMD SVM), чтобы вернуть ещё немного CPU. Пользуешься — оставь включённой.",
                         "«Ізоляція ядра / Цілісність пам'яті» (VBS) працює через віртуалізацію і стабільно з'їдає CPU — на старих чипах це помітно в CS2. Саму ізоляцію вимикає наш твік hvci-off. А в BIOS: якщо ти не користуєшся віртуальними машинами, WSL, Docker чи пісочницею Windows — можна додатково вимкнути віртуалізацію (Intel VT-x / AMD SVM). Користуєшся — залиш увімкненою.",
                         "Core Isolation / Memory Integrity (VBS) runs through virtualization and steadily costs CPU — noticeable in CS2 on older chips. Our hvci-off tweak turns the isolation itself off. In BIOS: if you don't use virtual machines, WSL, Docker or Windows Sandbox, you can additionally disable virtualization (Intel VT-x / AMD SVM) to recover a bit more CPU. If you do use them, leave it on.")));
        }
        else
        {
            tips.Add(new BiosTip(BiosTipLevel.Optional,
                new L10n("Виртуализация (VT-x/SVM) — если не нужна",
                         "Віртуалізація (VT-x/SVM) — якщо не потрібна",
                         "Virtualization (VT-x/SVM) — if unused"),
                new L10n("Если ты не пользуешься виртуальными машинами, WSL, Docker или песочницей Windows — виртуализацию (Intel VT-x / AMD SVM) можно выключить в BIOS, чтобы вернуть немного CPU. Если пользуешься — оставь включённой.",
                         "Якщо ти не користуєшся віртуальними машинами, WSL, Docker чи пісочницею Windows — віртуалізацію (Intel VT-x / AMD SVM) можна вимкнути в BIOS. Якщо користуєшся — залиш увімкненою.",
                         "If you don't use virtual machines, WSL, Docker or Windows Sandbox, virtualization (Intel VT-x / AMD SVM) can be disabled in BIOS to recover some CPU. If you do use them, leave it on.")));
        }

        // 4) Профиль питания CPU в BIOS — ровнее частота.
        tips.Add(new BiosTip(BiosTipLevel.Optional,
            new L10n("Стабильная частота CPU в BIOS",
                     "Стабільна частота CPU в BIOS",
                     "Steady CPU clocks in BIOS"),
            new L10n("Для ровного фреймтайма отключи в BIOS глубокий сон ядер — ищи пункт «CPU C States» / «Global C-state Control» / «Package C State Limit» и поставь Disabled. Заодно план питания CPU на максимум («High Performance» / «Max Performance»). Частота перестанет гулять — меньше микрофризов. Следи за температурой CPU. (Названия в BIOS всегда на английском — по-русски их не будет.)",
                     "Для рівного фреймтайму вимкни в BIOS глибокий сон ядер — шукай пункт «CPU C States» / «Global C-state Control» / «Package C State Limit» і постав Disabled. Заодно план живлення CPU на максимум («High Performance» / «Max Performance»). Частота перестане гуляти — менше мікрофризів. Слідкуй за температурою CPU. (Назви в BIOS завжди англійською.)",
                     "For steadier frametime, in BIOS disable deep core sleep — look for 'CPU C States' / 'Global C-state Control' / 'Package C State Limit' and set it to Disabled. Also set the CPU power plan to max ('High Performance' / 'Max Performance'). Clocks stop wandering — fewer micro-stutters. Watch CPU temps.")));

        // 4b) Топология CPU: гибрид Intel / много-CCD AMD — известная болячка планирования CS2.
        var topo = CpuTopology.Detect(hw);
        if (topo.Kind == CpuTopologyKind.IntelHybrid)
        {
            tips.Add(new BiosTip(BiosTipLevel.Recommended,
                new L10n("E-ядра мешают CS2 (Intel-гибрид)",
                         "E-ядра заважають CS2 (Intel-гібрид)",
                         "E-cores hurt CS2 (Intel hybrid)"),
                new L10n("У тебя гибридный CPU (P + E ядра). Source 2 иногда уводит главный поток на медленные E-ядра → просадки и стуттер. Наш твик «CS2 на правильные ядра» держит игру на P-ядрах софтом. Надёжнее — выключить E-ядра в BIOS на время игры: ищи «CPU E-cores» / «Efficient-cores» → Disabled, у ASUS есть «Legacy Game Compatibility Mode». (Названия в BIOS английские.)",
                         "У тебе гібридний CPU (P + E ядра). Source 2 іноді тягне головний потік на повільні E-ядра → просадки та стуттер. Наш твік «CS2 на правильні ядра» тримає гру на P-ядрах софтом. Надійніше — вимкнути E-ядра в BIOS: шукай «CPU E-cores» / «Efficient-cores» → Disabled, в ASUS є «Legacy Game Compatibility Mode».",
                         "You have a hybrid CPU (P + E cores). Source 2 sometimes parks its main thread on slow E-cores → drops and stutter. Our 'Pin CS2 to the right cores' tweak keeps it on P-cores in software. More reliable: disable E-cores in BIOS — look for 'CPU E-cores' / 'Efficient-cores' → Disabled (ASUS: 'Legacy Game Compatibility Mode').")));
        }
        else if (topo.Kind == CpuTopologyKind.AmdMultiCcd)
        {
            bool x3d = hw.CpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase);
            tips.Add(new BiosTip(BiosTipLevel.Recommended,
                new L10n("Много-CCD Ryzen: держи CS2 на быстром CCD",
                         "Багато-CCD Ryzen: тримай CS2 на швидкому CCD",
                         "Multi-CCD Ryzen: keep CS2 on the fast CCD"),
                new L10n((x3d
                    ? "У тебя X3D с двумя CCD: 3D-кэш только на первом, и если CS2 уедет на второй CCD — теряешь и кэш, и FPS. Поставь свежий AMD Chipset Driver (в нём «3D V-Cache Performance Optimizer» сам держит игры на кэш-CCD) и в BIOS включи «CPPC» + «CPPC Preferred Cores» (Enabled/Auto). "
                    : "У тебя Ryzen с двумя CCD: переход потока между CCD добавляет задержку → стуттер в CS2. В BIOS включи «CPPC» + «CPPC Preferred Cores» (Enabled/Auto). Как вариант — оставить 1 CCD («CCD Control» → 1) на время игры. ")
                    + "Наш твик «CS2 на правильные ядра» уже сажает игру на первый CCD софтом.",
                    (x3d
                    ? "У тебе X3D з двома CCD: 3D-кеш лише на першому. Постав свіжий AMD Chipset Driver («3D V-Cache Performance Optimizer») і в BIOS увімкни «CPPC» + «CPPC Preferred Cores» (Enabled/Auto). "
                    : "У тебе Ryzen з двома CCD: перехід потоку між CCD додає затримку. У BIOS увімкни «CPPC» + «CPPC Preferred Cores». Як варіант — 1 CCD («CCD Control» → 1). ")
                    + "Наш твік «CS2 на правильні ядра» вже садить гру на перший CCD софтом.",
                    (x3d
                    ? "You have an X3D with two CCDs: the 3D cache is only on the first. Install the latest AMD Chipset Driver ('3D V-Cache Performance Optimizer' keeps games on the cache CCD) and enable 'CPPC' + 'CPPC Preferred Cores' (Enabled/Auto) in BIOS. "
                    : "You have a dual-CCD Ryzen: threads bouncing across CCDs add latency → CS2 stutter. Enable 'CPPC' + 'CPPC Preferred Cores' in BIOS, or run 1 CCD ('CCD Control' → 1). ")
                    + "Our 'Pin CS2 to the right cores' tweak already keeps the game on the first CCD in software.")));
        }

        // 5) Прочее (Fast Boot / HPET / обновление BIOS).
        tips.Add(new BiosTip(BiosTipLevel.Optional,
            new L10n("Мелочи: Fast Boot, HPET, обновление BIOS",
                     "Дрібниці: Fast Boot, HPET, оновлення BIOS",
                     "Extras: Fast Boot, HPET, BIOS update"),
            new L10n("Включи Fast Boot (быстрее старт). Если BIOS даёт опцию HPET — оставь выключенным (мы и так правим таймеры в Windows). Свежий BIOS может добавить стабильности памяти, но учти: новый микрокод иногда возвращает часть защит Spectre — если критичен максимум FPS, взвесь.",
                     "Увімкни Fast Boot (швидший старт). Якщо BIOS дає опцію HPET — залиш вимкненим (ми й так правимо таймери у Windows). Свіжий BIOS може додати стабільності пам'яті, але врахуй: новий мікрокод іноді повертає частину захистів Spectre.",
                     "Enable Fast Boot (faster startup). If BIOS exposes HPET — leave it off (we already tune timers in Windows). A newer BIOS can improve memory stability, but note: new microcode sometimes reinstates part of the Spectre mitigations.")));

        return tips;
    }
}
