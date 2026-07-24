# Универсализация: от «твиков под мой ПК» к адаптивному оптимизатору

**Цель:** превратить фиксированный набор твиков в программу, которая под ЛЮБОЙ (как правило слабый)
ПК определяет узкое место и подстраивает и рекомендации, и профили. Ниже — собранная база знаний и
дизайн будущих твиков. **Ничего из раздела «Будущие твики» пока не интегрировано** — это план на «сверимся завтра».

Статус: 📊 подготовка (2026-07-22). Все проценты — из открытых гайдов/бенчей (ссылки в конце), ориентир.

---

## 1. Движок определения узкого места (ядро универсализации)

### 1.1 Что уже знаем о железе (`HardwareInfo`)
CPU (имя/поколение/ядра/потоки), GPU (вендор/модель), RAM (ГБ/МГц/разнокалиберность), монитор Гц,
ОС/билд, включён ли VBS/HVCI. Надо ДОБАВИТЬ детекты:
- **Каналы RAM** (одно/двухканал): по числу заполненных слотов/`BankLabel`/`DeviceLocator` в
  `Win32_PhysicalMemory`. Одноканал на слабом ПК = **−20–40% FPS** в CS2 — критично.
- **Тип накопителя с игрой** (SSD/HDD): по диску, где лежит cs2.exe (`MSFT_PhysicalDisk.MediaType`).
- **iGPU-only** (нет дискретной): GpuVendor = Intel/встройка.
- **Свободная RAM / объём** (уже есть через GlobalMemoryStatusEx).

### 1.2 Runtime-сигнал: CPU-bound vs GPU-bound (через PresentMon)
Метрика **GPU Busy** (мс активной работы GPU в кадре) vs **FrameTime**:
- `GPUBusy / FrameTime ≳ 0.90` → **GPU-bound** (GPU занят почти весь кадр).
- `GPUBusy / FrameTime ≲ 0.70` → **CPU/системный bound** (GPU простаивает, ждёт CPU).
- Между — сбалансировано.
Простой прокси без PresentMon: загрузка GPU < 80% при целевом FPS → CPU-bound.
**TODO движка:** в `PresentMonService.Parse` добавить чтение колонки GPU-busy (PresentMon 2.x:
`msGPUActive`/`GPUBusy`) → в `FrametimeStats` поле GpuBusyMs → авто-вердикт узкого места.

### 1.3 Тепловой троттлинг (будущее)
Читать температуру/флаг Thermal Throttling (LibreHardwareMonitorLib или HWiNFO shared memory) во время
замера. Падение частоты + высокая t° после 15–20 мин → сценарий «перегрев» (см. [[../docs/THROTTLESTOP.md]]).

---

## 2. Матрица «сценарий → решение»

| # | Сценарий (условие детекта) | Наши системные твики (акцент) | BIOS/железо | Внутриигровое (подсказки) | Внешнее |
|---|---|---|---|---|---|
| S1 | **CPU-bound** (GPUBusy≪FrameTime; ядер мало/старый CPU) — дефолт CS2 | весь агрессив: spectre-off, hvci-off, core-parking, cpu-idle, priority/IFEO, timers, mitigations | XMP, C-states, max perf | Shader Detail=Low (+5–15%), AO off + Particle low (+10–20%), тени Very Low, 4:3 1024×768 | — |
| S2 | **GPU-bound** (GPUBusy≈FrameTime; слабый дискретный GPU) | GPU: PowerMizer max, low-latency, HAGS, MPO-off, MSI-mode. CPU-твики менее важны | — | ↓ разрешение/рендер-скейл, Volumetric Smokes=Low (тяжёлые для GPU), MSAA off, тени | новый драйвер |
| S3 | **Мало RAM** (≤8 ГБ / свободно <15%) | standby-clean, svchost-group, prefetch-off, memory-compression (взвесить), background/xbox off | +16 ГБ, XMP | текстуры Low | закрыть фон |
| S4 | **Одноканальная RAM** (1 планка) | — (софтом не решить) | ⚠ поставить 2-ю одинаковую планку = **+20–40%** — сильнейший рычаг | — | — |
| S5 | **Медленная RAM** (ниже паспорта, ≤2400) | — | XMP/DOCP = **+8–12%** | — | — |
| S6 | **Игра на HDD** | prefetch-off осторожно | перенести CS2 на SSD (любой SATA = огромный скачок) | — | дефраг, ≥20% свободно, ReadyBoost как костыль |
| S7 | **Перегрев/троттлинг** (t° высокая, частота падает) | cpu-idle/parking (ровнее), power-plan | репаста, чистка, fan curve, power limits | — | **undervolt** ThrottleStop (ноут, −80…−120 мВ) / Ryzen Master |
| S8 | **Только iGPU** (встройка) | gpu-preference неактуален; всё CPU + RAM | XMP критичен для iGPU | минимум графики, ↓разрешение | реалистичные ожидания |
| S9 | **Старый/битый драйвер GPU** | — | — | — | DDU + чистая установка драйвера |
| S10 | **VBS/HVCI включён** | hvci-off (наш твик) | выключить изоляцию ядра | — | +5–10% (иногда 10–25%) |

### 2.1 Ключевые факты для логики
- CS2 **почти всегда CPU-bound** (Source 2 грузит 1–2 ядра) — S1 самый частый, поэтому наш текущий
  набор уже «в яблочко» для большинства.
- **Одноканал → двухканал** и **XMP** — крупнейшие рычаги, но они в железе/BIOS (мы только советуем).
- **HAGS + VBS-off** дают в сумме заметный прирост почти всем — у нас уже есть.
- «30–50% прироста» из гайдов = это СУММА системных твиков + внутриигровых настроек + XMP, а не один твик.

---

## 3. Будущие твики — СТАТУС ИНТЕГРАЦИИ (обновлено 2026-07-22)

| id | Что делает | Механизм | Риск | Условие | Статус |
|---|---|---|---|---|---|
| `pagefile-fixed` | Фикс. размер файла подкачки (8 ГБ, min=max) на системном диске | реестр `Memory Management\PagingFiles` + `AutomaticManagedPagefile` | Medium | RAM ≤ 8 ГБ | ✅ **интегрирован** ([PagefileFixedTweak.cs](../src/Source2Boost.Core/Tweaks/PagefileFixedTweak.cs)) |
| `interrupt-affinity` | Увести прерывания GPU с CPU0 (DevicePolicy=4 + маска) | реестр device `Interrupt Management\Affinity Policy` | High | дискретная GPU + ≥4 потока | ✅ **интегрирован** ([InterruptAffinityTweak.cs](../src/Source2Boost.Core/Tweaks/InterruptAffinityTweak.cs)) |
| `amd-radeon-max` | ULPS off (аналог PowerMizer для AMD) | реестр Radeon | Medium | GpuVendor=AMD | ✅ интегрирован ранее |
| `timer-holder` | Стабильный высокоточный таймер | реестр `GlobalTimerResolutionRequests=1` | Medium | всем | ✅ покрыт твиком `timer-resolution-global` (реестром чище резидентного процесса) |
| `game-on-ssd-warn` | Предупреждение, если cs2.exe на HDD | детект диска | инфо | игра на HDD | ✅ покрыт как **finding** в BottleneckAnalyzer (S6) |
| `intel-igpu-tune` | Настройки встройки Intel | реестр | Medium | iGPU-only | ✅ покрыт как **finding** (совет по XMP/разрешению); чистого безопасного реестр-твика под iGPU нет — не делаем плацебо |
| `thermal-card` | Детект троттлинга | LibreHardwareMonitor | инфо | перегрев | ⏸ отложено: требует внешней библиотеки (LibreHardwareMonitorLib) — обсудить, тянуть ли зависимость |
| `nvapi-profile` | Нативный профиль NVIDIA (Reflex/Threaded Opt) | nvapi64.dll P/Invoke | Medium | NVIDIA | ⏸ отложено: частично дублирует `nvidia-low-latency`/`nvidia-max-perf`, требует хрупкого P/Invoke |
| `cs2-video-preset` | Генерация внутриигрового конфига графики | videoconfig | Safe | по узкому месту | ⏸ по решению пользователя НЕ делаем |
| `background-apps-perapp` | Точечное отключение UWP | реестр per-app | Safe | всем | ✅ глобально покрыт твиком `background-apps-off` |

Уже покрытое реестром (не переделывать): Prefer Max Perf, low-latency, HAGS, MSI, MPO, power throttling, глобальный таймер, network throttling, фон.

**Итог интеграции универсализации:** все реалистично-полезные и безопасные твики из плана добавлены. Осталось только то, что (а) требует внешних зависимостей (`thermal-card`), (б) дублирует существующее (`nvapi-profile`), или (в) отложено пользователем (`cs2-video-preset`).

---

## 4. Дизайн адаптивности (как это подать в UI)

1. **Авто-вердикт на Дашборде**: после железо-скана + (опц.) замера показать «Твоё узкое место:
   процессор / видеокарта / память / накопитель / перегрев» + 1–2 главные рекомендации под него.
2. **Профили остаются** (Безопасный/Оптимальный/Максимум), но:
   - применимость твика фильтруется по сценарию (напр. `amd-radeon-max` только на AMD — уже есть механизм `supported`);
   - добавить пометку «рекомендовано для твоего железа» на релевантные твики;
   - «Оптимальный» может авто-доукомплектовываться сценарными твиками.
3. **Раздел «Что мне сделать вручную»**: агрегирует BIOS-советы + внешние (XMP, вторая планка, SSD,
   undervolt, DDU) в один чек-лист под конкретный ПК — это львиная доля прироста на слабом железе.
4. **Реалистичные ожидания**: показывать честный потолок под железо (у нас уже есть `FpsEstimator`).

---

## 5. Приоритеты на завтра (черновик обсуждения)
1. Движок: детект каналов RAM + типа накопителя + GPU-busy из PresentMon → авто-вердикт узкого места. (высокий эффект, низкий риск)
2. AMD/Intel ветки (`amd-radeon-max`, low-latency-аналоги) — сейчас мы NVIDIA-центричны, а ЦА разношёрстная.
3. `pagefile-fixed` + `game-on-ssd-warn` для сценариев RAM/HDD.
4. Агрегированный чек-лист «вручную» (BIOS + внешнее).
5. (Позже, по решению) `cs2-video-preset`, `interrupt-affinity`, `timer-holder`, `nvapi-profile`, `thermal-card`.

## Источники
- [WindowsForum: диагностика узкого места (PresentMon/GPU Busy)](https://windowsforum.com/threads/diagnose-gaming-bottlenecks-use-resolution-scaling-frame-time-presentmon.428601/)
- [wccftech: CPU или GPU bottleneck](https://wccftech.com/how-to/cpu-or-gpu-bottleneck-how-to-diagnose-whats-really-limiting-your-gaming-performance/)
- [Steam: CS2 hardware performance 2026](https://steamcommunity.com/sharedfiles/filedetails/?id=3673242775)
- [blog.cs2: CS2 CPU or GPU intensive](https://blog.cs2.ad/is-cs2-cpu-or-gpu-intensive/)
- [Hone: CS2 stuttering fixes](https://hone.gg/blog/counter-strike-2-stuttering-fixes/)
- [bottleneckcalculator: shader compilation fix](https://bottleneckcalculator.us.com/knowledge-base/gaming-performance/shader-compilation-fix/)
- [SmoothFPS: thermal throttling](https://smoothfps.com/guides/thermal-throttling)
