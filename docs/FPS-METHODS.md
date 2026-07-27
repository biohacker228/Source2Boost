# Source2Boost — полный каталог методов повышения FPS в CS2

Цель: выжать максимум из **конкретного железа пользователя** — i7-6700 (Skylake, 4c/8t, non-K),
GTX 1650 SUPER, DDR3-1866 (разнокалиберный набор — узкое место), монитор 180 Гц.
CS2 (Source 2) на этой связке **упирается в CPU** и в память → рваный фреймтайм, avg 100–140 FPS.

Легенда риска: 🟢 Safe (обратимо, без риска) · 🟡 Medium (обратимо, нужен ребут/влияет на систему) · 🔴 High (агрессивно/безопасность/стабильность).
Статус: ✅ реализовано · 🔧 в работе (ночной агент) · 📋 план · 💡 совет пользователю (не автоматизируется).

---

## 0. Честная вводная (ожидания)
Софтовые твики на этом железе имеют **потолок ~15–30% avg** и, что важнее, дают **более ровные 1%/0.1% low**
(меньше стуттеров) — а не гигантский прирост среднего. «Чуть лучше + плавнее» после базовых твиков — это норма.
Крупные скачки дают ТРИ вещи, которые перевешивают весь остальной список:
1. 🔴 **Отключение митигаций Spectre/Meltdown** — на Skylake (6-е поколение Intel) это возвращает **5–15% CPU**.
   Для CPU-bound CS2 это самый жирный софтовый выигрыш. (Ниже, п. 1.1.)
2. 💡 **BIOS: XMP/частота+тайминги RAM** — память здесь главный лимит; софтом не чинится.
3. 💡 **Внутриигровые настройки видео + параметры запуска** — иногда больше, чем все системные твики вместе.

---

## 1. CPU (главный лимит на этой машине)

### 1.1 🔴 Отключить митигации Spectre/Meltdown/MDS — САМЫЙ ЖИРНЫЙ СОФТ-ВЫИГРЫШ 🔧
- Что: убрать программные заплатки спекулятивного выполнения (замедляют syscalls/переключения контекста).
- Как: `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management`
  `FeatureSettingsOverride`=3 (DWORD), `FeatureSettingsOverrideMask`=3 (DWORD). Ребут.
- Риск: 🔴 снижает защиту от спекулятивных атак. Для игрового ПК многие идут на это осознанно. Явное предупреждение + откат.
- Эффект: **+5–15% CPU** на Skylake → напрямую в FPS и в 1% low.
- Профиль: Benchmark. RequiresRestart.

### 1.2 🟡 Полностью отключить парковку ядер (core parking) 🔧
- Как: план питания — атрибут `CPMINCORES`=100% через powercfg, либо реестр
  `HKLM\...\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583` `ValueMax`=0.
- Эффект: клоки не гуляют → ровнее фреймтайм. (Ultimate-план уже частично закрывает.)

### 1.3 🟡 Минимальное состояние процессора 100% / отключить C-states-троттлинг 🔧
- Как: powercfg PROCTHROTTLEMIN=100, PROCTHROTTLEMAX=100. Держит частоту стабильной.

### 1.4 🟡 Отключить динамический тик и HPET, TSC как источник 🔧
- Как: `bcdedit /set disabledynamictick yes`, `bcdedit /set useplatformclock false`, `bcdedit /set tscsyncpolicy Enhanced`.
- Эффект: ниже латентность таймера, ровнее фрейм-пейсинг. Риск: редко влияет на стабильность — обратимо.

### 1.5 🟡 Win32PrioritySeparation=0x26 ✅ (реализовано)
### 1.6 🟡 Timer resolution 0.5мс (GlobalTimerResolutionRequests) ✅ (реализовано; резидентный холдер — 📋)
### 1.7 🔴 Аффинити/приоритет cs2.exe: -high + привязка к физическим ядрам 📋
- Через параметры запуска `-high` и/или лаунчер, задающий affinity/priority процессу cs2.exe.

---

## 2. Память (узкое место — в основном BIOS)
### 2.1 💡 Включить XMP/выставить максимальную общую частоту и тайминги вручную (BIOS)
- Разнокалиберный набор часто стартует на 1333/1600. Вручную поднять до общего максимума + подтянуть тайминги.
### 2.2 🔴 DisablePagingExecutive=1, LargeSystemCache 🔧 (осторожно, влияет на память)
- `HKLM\...\Memory Management` `DisablePagingExecutive`=1. Держит драйверы/ядро в RAM.
### 2.3 🟡 Отключить сжатие памяти/Superfetch ✅ (service-live-stop: SysMain)
### 2.4 💡 Файл подкачки: системный на SSD, не отключать (стабильность).

---

## 3. GPU (GTX 1650 SUPER)
### 3.1 🟡 NVIDIA: Prefer Maximum Performance (без даунклока в простое) 🔧
- Реестр профиля/через NVAPI: PowerMizer в максимум. Эффект: нет провалов частоты GPU.
### 3.2 🟡 NVIDIA Low Latency = Ultra (аналог anti-lag) 🔧/💡
- Часть — через драйвер; в игре включить **NVIDIA Reflex** (главное для латентности).
### 3.3 🟡 MSI-mode для прерываний GPU 🔧
- `HKLM\SYSTEM\...\PCI\<gpu>\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties` `MSISupported`=1.
- Эффект: ниже латентность прерываний, ровнее кадры.
### 3.4 🟡 HAGS (Hardware GPU Scheduling) — СПОРНО, вынесен в Лабораторию (gpu-hags-off)
- Ранее форсили ВКЛючение в профилях. Ресёрч (2026-07): для соревновательной CS2 на старых
  GPU / Windows 10 (наша ЦА) ВЫКЛючение часто даёт ровнее фреймтайм и меньше статтера; на новых
  GPU / Windows 11 иногда лучше ВКЛючённым — эффект ЖЕЛЕЗОЗАВИСИМЫЙ. Поэтому не дефолт, а
  A/B-эксперимент в Лаборатории (`gpu-hags-off`, HwSchMode=1), проверяемый бенчмарком.
### 3.5 🟢 Чистка кэша шейдеров ✅ (ежедневная задача, реализовано)
### 3.6 🟡 Отключить NVIDIA-телеметрию (службы/задачи) 🔧
### 3.7 💡 Драйвер: чистая установка через DDU, свежая Game Ready версия.

---

## 4. Windows / система
### 4.1 🔴 Исключение cs2.exe в Microsoft Defender (real-time сканирование не трогает игру) 🔧
- Add-MpPreference -ExclusionProcess cs2.exe / -ExclusionPath <cs2 dir>. Реальный прирост на слабом CPU. Откат: Remove-MpPreference.
### 4.2 🟢 Игровой режим Windows ✅ · 4.3 🟢 Xbox DVR off ✅ · 4.4 🟢 FSO off для cs2 ✅
### 4.5 🟢 Эффекты Windows → быстродействие ✅ · 4.6 🟢 Акселерация мыши off ✅
### 4.7 🟡 NetworkThrottlingIndex=off ✅ · SystemResponsiveness=0 ✅ · Nagle off 🔴✅
### 4.8 🟡 Отключить фоновые приложения (UWP) и лишние автозагрузки 📋
### 4.9 🟢 Чистка temp/prefetch/DNS перед сессией 📋
### 4.10 🟡 План питания Ultimate Performance ✅ (power-plan-max)
### 4.11 🔴 Отключить лишние службы (сверх SysMain/DiagTrack): WSearch, DPS, лишнее 📋 (осторожно)

---

## 5. CS2-специфика (config + launch) — часто самый большой рычаг 📋
### 5.1 Параметры запуска (валидные для Source 2)
`-novid -console -high -nojoy -fullscreen +fps_max 0 -softparticlesdefaultoff +exec autoexec`
- Осторожно/тест: `-vulkan` (иногда ровнее на NVIDIA; проверять A/B), `-threads` НЕ трогать (движок сам).
- НЕ тащить CSGO-мусор: `-d3d9ex`, `mat_queue_mode`, `-tickrate`, `cl_interp/cmdrate/updaterate`.
### 5.2 autoexec.cfg (валидные cvar)
`fps_max 0`, `fps_max_ui 120`, `engine_no_focus_sleep 0`, `cl_forcepreload 1`,
`cl_disable_ragdolls 1`, `r_drawtracers_firstperson 0`, отключить лишние партиклы/декали.
### 5.3 💡 Настройки видео в игре (самое влияющее)
- Тени: низко/выкл, Model/Texture detail: низко, Shader detail: низко, MSAA: off/CMAA2,
  Multicore rendering: ON, Boost Player Contrast: on (видимость), разрешение/масштаб — под FPS.
- **NVIDIA Reflex: Enabled/On+Boost** — критично для латентности.

---

## 6. BIOS/железо (советы, не автоматизируется) 💡
- XMP/частота RAM (п.2.1) — приоритет №1 по железу.
- Отключить C-states/EIST для стабильных клоков (если готов к нагреву/энергопотреблению).
- Обновить BIOS (микрокод) — но новый микрокод может вернуть Spectre-замедление; баланс с п.1.1.
- Термопаста/чистка — троттлинг по температуре съедает буст.

---

## 7. Новые находки (2026-07-21, к внедрению)
### 7.1 🟢 Форсировать дискретную GPU + High Performance для cs2.exe (Windows Graphics) — ПРОСТО И БЕЗОПАСНО 📋
- `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, имя значения = полный путь к cs2.exe,
  данные = `GpuPreference=2;` (2 = High performance). Гарантирует, что игра идёт на GTX 1650S, не на встройке, в макс-режиме.
- Риск 0, обратимо. Аналог «Graphics settings → cs2 → High performance» в Windows.
### 7.2 🟡 Постоянный высокий приоритет cs2.exe через IFEO (без -high в launch) 📋
- `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\cs2.exe\PerfOptions`
  `CpuPriorityClass`=3 (High). Чище, чем `-high`: приоритет применяется всегда, движок сам не спорит с планировщиком.
- Обратимо (удалить ключ PerfOptions). На CPU-bound даёт стабильнее фреймтайм.
### 7.3 🟡 Interrupt affinity: увести прерывания устройств с ядра 0 📋
- Через `...\Device Parameters\Interrupt Management\Affinity Policy` — разгрузить ядро, где сидит основной поток игры. Тонко, тестировать.
### 7.4 💡 Тест рендера `-vulkan` (A/B) — иногда ровнее фреймтайм на NVIDIA
### 7.5 💡 Внешний кап фреймтайма (RTSS/Reflex) для максимально ровных кадров вместо fps_max 0
### 7.6 🟡 powercfg: USB selective suspend off, PCIe ASPM off — периферия/шина всегда на полной мощности 📋

## Приоритет внедрения (по соотношению «выигрыш/риск» для ЭТОЙ машины)
1. 🔴 Spectre off (1.1) — самый жирный.  2. 💡 XMP RAM (2.1).  3. 📋 CS2 launch+autoexec+видео (гл.5).
4. 🟡 NVIDIA max perf + Reflex (3.1–3.2).  5. 🔴 Defender exclusion (4.1).  6. 🟡 core parking/C-states (1.2–1.4).
7. 🟡 MSI-mode GPU (3.3).  Остальное — по мелочи в ровность фреймтайма.

---

## §8 Методы про-настройщиков (research)

Свод методов, которыми пользуются про-игроки, их технические тренеры и известные гайды по
CS2/латентности (NVIDIA, Profile Inspector, ISLC, Process Lasso, BIOS-твики). Для каждого:
**что делает · как · риск · статус в Source2Boost** (✅ уже покрыт — с ID твика · 🆕 кандидат к внедрению · 💡 ручное/BIOS, не автоматизируется).
Легенда статуса автоматизации: **AUTO** — можно сделать из приложения · **MANUAL/BIOS** — только руками пользователя.

Дата свода: 2026-07-21. Многие пункты — общеизвестные практики сообщества; NVIDIA Reflex, Profile Inspector и BIOS-настройки по своей природе выполняются пользователем вручную.

### 8.1 NVIDIA Reflex (Low Latency / Boost) — 💡 MANUAL (в игре)
- Что: сокращает конвейер рендера, снижает end-to-end латентность мыши→экран. Самый важный «латентностный» тумблер для соревновательного CS2.
- Как: в самой CS2 → Настройки видео → NVIDIA Reflex = **Enabled/On + Boost**. Драйверный Low Latency — запасной вариант, если в игре нет.
- Риск: 🟢 нет. Не автоматизируется (внутриигровая опция).
- Статус: 💡 MANUAL. Совет пользователю. Пересекается с 3.2.

### 8.2 NVIDIA Profile Inspector: Low Latency = Ultra, Max Prerendered Frames = 1, Threaded Optimization = On, Power = Prefer Max Performance — частично ✅ / 🆕
- Что: драйверные настройки per-app профиля CS2: Ultra Low Latency (=Reflex-подобное поведение вне игр), лимит предрендеренных кадров = 1 (меньше input lag), Threaded Optimization On (многопоточный драйвер), режим питания «максимальная производительность».
- Как: внешняя утилита **NVIDIA Profile Inspector** (orbmu2k) + импорт .nip-профиля, ЛИБО программно через **NVAPI** (`NvAPI_DRS_*`).
- Риск: 🟡 обычно безопасно; неверный профиль может снизить FPS. Обратимо (сброс профиля).
- Статус: **Power=Max Performance** частично ✅ покрыт `nvidia-max-perf` (но тот пишет PowerMizer в class-ключ реестра, а не в DRS-профиль). **Low Latency Ultra / Prerendered Frames=1 / Threaded Optimization** — 🆕 AUTO-кандидат (реализовать через NVAPI DRS или поставку .nip). Пересекается с 3.1–3.2.

### 8.3 Timer resolution 0.5 мс — резидентный holder — частично ✅ / 🆕
- Что: удерживает системный таймер на 0.5 мс постоянно (Windows 10/11 после 2004 сбрасывает разрешение для фоновых процессов; нужен резидентный процесс, вызывающий `NtSetTimerResolution`).
- Как: реестр `GlobalTimerResolutionRequests`=1 (глобально разрешает) + маленький фоновый процесс-холдер, держащий 0.5 мс.
- Риск: 🟡 обратимо.
- Статус: реестровая часть ✅ `timer-resolution-global`. **Резидентный holder-процесс** — 🆕 AUTO-кандидат (аналог `--clean-standby`: фоновый режим приложения, держащий разрешение). Пересекается с 1.6.

### 8.4 Очистка standby-списка памяти (ISLC-подобно) — ✅ РЕАЛИЗОВАНО
- Что: периодический сброс standby-кэша памяти при нехватке свободной RAM убирает микро-стуттер (переполненный кэш ожидания вызывает всплески фреймтайма).
- Как: `NtSetSystemInformation(SystemMemoryListInformation=0x50, MemoryPurgeStandbyList=4)` под привилегией `SeProfileSingleProcessPrivilege`; запускается по таймеру. (Аналог Intelligent Standby List Cleaner.)
- Риск: 🟢 обратимо (удаление задачи).
- Статус: ✅ AUTO — **`standby-clean`** (`StandbyCleanTweak`): задача планировщика `Source2Boost_StandbyClean` каждые 5 минут → `Source2Boost.exe --clean-standby`, чистит только при available < 15% ИЛИ < 2 ГБ. **Заменил вредный shader-таймер.**

### 8.5 Отключение сжатия памяти (Memory Compression) — ✅ РЕАЛИЗОВАНО
- Что: Windows перестаёт тратить CPU на сжатие/разжатие страниц RAM в фоне; на CPU-bound связке освобождает процессорное время.
- Как: `Disable-MMAgent -MemoryCompression` (PowerShell). Эффект после ребута.
- Риск: 🟡 чуть больше расход RAM/подкачки. Обратимо (`Enable-MMAgent`).
- Статус: ✅ AUTO — **`memory-compression-off`** (`MemoryCompressionTweak`). Пересекается с 2.3 (SysMain уже гасится `service-live-stop`).

### 8.6 Отключить HPET / динамический тик / platform clock — ✅ РЕАЛИЗОВАНО (bcdedit)
- Что: убрать высокоточный таймер событий как источник и снять динамический тик — ниже латентность таймера, ровнее фрейм-пейсинг.
- Как: `bcdedit /set disabledynamictick yes`, `bcdedit /set useplatformclock false`, `tscsyncpolicy Enhanced`. Доп. можно отключить HPET-устройство в Диспетчере устройств (это уже ручное).
- Риск: 🔴 редко влияет на стабильность; обратимо, нужен ребут.
- Статус: ✅ AUTO — **`bcdedit-timers`**. Отключение HPET-устройства в Device Manager — 💡 MANUAL (доп., обычно не требуется). Пересекается с 1.4.

### 8.7 MSI-режим прерываний GPU — ✅ РЕАЛИЗОВАНО
- Что: Message-Signaled Interrupts для видеокарты — ниже латентность прерываний.
- Как: реестр `...\PCI\<gpu>\...\MessageSignaledInterruptProperties MSISupported`=1.
- Риск: 🔴 обратимо, ребут.
- Статус: ✅ AUTO — **`msi-mode-gpu`** (консервативный матч только по дисплейному адаптеру NVIDIA). Пересекается с 3.3.

### 8.8 Отключить парковку ядер + держать состояние CPU 100% — ✅ РЕАЛИЗОВАНО (частично)
- Что: ядра не паркуются, частота не гуляет → ровнее фреймтайм.
- Как: powercfg `CPMINCORES`=100, `PROCTHROTTLEMIN/MAX`=100 на активной схеме.
- Риск: 🟡 обратимо.
- Статус: ✅ AUTO — **`core-parking-off`**. **C-states / EIST off** — 💡 MANUAL/BIOS (нельзя из ОС надёжно). Пересекается с 1.2–1.3, 6.

### 8.9 Отключить полноэкранные оптимизации (FSO) — ✅ РЕАЛИЗОВАНО
- Что: стабильнее фреймтайм в fullscreen для cs2.exe.
- Как: реестр AppCompatFlags\Layers → `~ DISABLEDXMAXIMIZEDWINDOWEDMODE`.
- Статус: ✅ AUTO — **`fso-disable-cs2`**. Пересекается с 4.4.

### 8.10 CPU-аффинити cs2.exe на физические ядра (мимо ядра 0) + high priority — частично ✅ / 🆕
- Что: привязать основной поток игры к физическим ядрам, увести с ядра 0 (там сидят системные прерывания) — метод Process Lasso; плюс постоянный high priority.
- Как: **Process Lasso** (внешний тул) для affinity/priority, ЛИБО лаунчер, задающий `ProcessorAffinity`. Приоритет можно через IFEO.
- Риск: 🟡 обратимо; аффинити тонко — нужно тестировать (на 4c/8t выгода спорна).
- Статус: **priority** ✅ покрыт `cs2-high-priority` (IFEO CpuPriorityClass=3). **Affinity на физ. ядра** — 🆕 AUTO-кандидат, но требует процесса-лаунчера (IFEO аффинити не задаёт). Пересекается с 1.7, 7.3.

### 8.11 Чистая установка драйвера через DDU (+ NVCleanstall) — 💡 MANUAL
- Что: убрать остатки старых драйверов/телеметрии, поставить чистый свежий Game Ready.
- Как: **DDU** в безопасном режиме → чистая установка драйвера.
- Риск: 🟡 сам процесс безопасен, но полностью ручной (перезагрузки в safe mode).
- Статус: 💡 MANUAL. Совет пользователю. Пересекается с 3.7.

### 8.12 XMP / частота-тайминги RAM, отключить HT в BIOS — 💡 MANUAL/BIOS
- Что: XMP/ручной разгон памяти — главный лимит этой машины (DDR3-разнокалиберный набор); HT off — спорно для CS2 (обычно НЕ рекомендуется на 4-ядерном i7).
- Как: только BIOS/UEFI.
- Риск: 🟡/🔴 стабильность; вне зоны автоматизации.
- Статус: 💡 MANUAL/BIOS. XMP — приоритет №1 по железу. HT off — не советуем на i7-6700 (движок Source 2 любит потоки). Пересекается с 2.1, 6.

### 8.13 Разовая чистка shader-кэша — ✅ переведён в ручной режим
- Что: разово стереть кэши шейдеров CS2/GPU (после крупного обновления или сбоя шейдеров).
- Как: `ShaderCacheTweak.CleanNow()`.
- **ВАЖНО:** прежний ЕЖЕДНЕВНЫЙ таймер чистки удалён как ВРЕДНЫЙ — постоянное стирание шейдеров ведёт к более долгой загрузке и стуттеру рекомпиляции. Твик убран из авто-профилей; апгрейд сносит старую задачу `Source2Boost_ShaderCacheClean`. Корректирует прежний пункт 3.5.
- Статус: ✅ AUTO (только по кнопке, не по таймеру) — `shader-cache-clean` вне каталога профилей.

### Итог §8 — что нового к внедрению (🆕 AUTO-кандидаты)
1. 🆕 NVAPI DRS-профиль CS2: Low Latency=Ultra, Max Prerendered Frames=1, Threaded Optimization=On (8.2).
2. 🆕 Резидентный holder таймера 0.5 мс (8.3) — по образцу headless-режима `--clean-standby`.
3. 🆕 Лаунчер с CPU-аффинити cs2.exe на физические ядра мимо ядра 0 (8.10).
Уже покрыто в этой итерации: 8.4 `standby-clean`, 8.5 `memory-compression-off`, 8.13 ручной shader-clean.
Только вручную/BIOS: 8.1 Reflex, 8.11 DDU, 8.12 XMP/HT, C-states (8.8).
