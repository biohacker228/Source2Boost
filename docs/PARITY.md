# Паритет функций: Source2Boost vs конкуренты

Сверка со **cs2-omz** (tomasfauro) и **CS2-Ultimate-Optimization** (Precision-Optimize) на 2026-07-21.
Легенда: ✅ есть · ➕ добавлено в этой итерации · ⚠️ намеренно не копируем (причина) · 🔜 кандидат.

## Совпадает / покрыто нами

| Функция | У конкурента | У нас |
|---|---|---|
| HPET / disabledynamictick | оба | ✅ `bcdedit-timers` (disabledynamictick=yes, useplatformclock=No) |
| Парковка ядер off | оба | ✅ `core-parking-off` (CPMINCORES/PROCTHROTTLE=100) |
| План питания High/Ultimate | оба | ✅ `power-plan-max` |
| MSI-режим GPU (= «MSI utility») | оба | ✅ `msi-mode-gpu` (MSISupported=1, только дисплейный NVIDIA) |
| Nagle off (TcpAckFrequency/TCPNoDelay) | cs2-omz | ✅ `nagle-off` |
| FSO off для cs2.exe | cs2-omz | ✅ `fso-disable-cs2` |
| Xbox GameDVR off | оба | ✅ `gamedvr-off` + `game-mode` |
| SysMain + DiagTrack off | оба | ✅ `service-live-stop` |
| DisablePagingExecutive=1 | cs2-omz | ✅ `disable-paging-executive` |
| Visual FX = performance | cs2-omz | ✅ `visual-fx-performance` |
| NVIDIA low-latency (nvlddmkm) | U.O. | ➕ `nvidia-low-latency` (EnableLowLatencyMode=1) |
| WSearch + Print Spooler off | cs2-omz | ➕ `extra-services-off` |
| FPS benchmark / парсер | U.O. v2 | ✅ Мониторинг (PresentMon) + оценка/прогноз |
| Авто-детект железа | оба | ✅ HardwareInfo |
| Полный revert | оба | ✅ бэкап на каждый твик + точка восстановления |

## Наши сверх конкурентов

`spectre-off` (самый жирный выигрыш на Skylake), `standby-clean` (ISLC-подобно, ежедневно),
`mmcss-games`, `system-responsiveness`, `mouse-accel-off`, `gpu-preference-cs2`,
`cs2-high-priority` (IFEO), `timer-resolution-global`, `gpu-hags`, `network-throttling-off`,
`memory-compression-off`, `nvidia-max-perf` (PowerMizer), `defender-exclusion-cs2`,
BIOS-советник по железу, оценка оптимизации 0–100, прогноз FPS, склонения RU/UK.

## Намеренно НЕ копируем (с причиной)

- ⚠️ **Ежедневная очистка шейдеров** (cs2-omz чистит кэш) — ВРЕДНО как регулярная операция:
  стирание шейдеров = дольше загрузки + стуттер рекомпиляции, прироста FPS нет. Оставили как
  ручное действие (`ShaderCacheTweak.CleanNow()` — «почистить после смены драйвера»).
- ⚠️ **TCP autotuning off** (cs2-omz) — спорно: режет пропускную способность, может УХУДШИТЬ
  на нестабильном канале. У нас затык по FPS (CPU), не по пингу.
- ⚠️ **DNS switcher** (cs2-omz) — это отдельная сетевая фича, не про FPS; свой пинг-тест у нас
  через Мониторинг.
- ⚠️ **LargeSystemCache=0 / SSD TRIM on** — как правило уже дефолт Windows, прироста нет.

## Кандидаты (🔜, обсудить)

- QoS DSCP-46 policy для cs2 (приоритет игровых пакетов) — реальная польза для онлайна, низкий риск.
- NIC-свойства через netsh/PowerShell (interrupt moderation off, EEE off, flow control off) —
  латентность сети; риск задеть стабильность адаптера, нужен аккуратный откат.
- UDP socket buffers (cs2-omz) — эффект спорный, сложно откатывать.

## Параметры запуска (проверено вебом 2026-07)

Итог: `-novid -console -high -nojoy -fullscreen -softparticlesdefaultoff -mainthreadpriority 2 +exec autoexec`

- `-mainthreadpriority 2` — ✅ легитимный, ровнее фреймтайм/1% low (добавлен).
- `+thread_pool_option 4` — ❌ несуществующий cvar (выдумка cs2-omz), НЕ добавляем.
- `-threads` — ❌ Source 2 сам раздаёт потоки, может ронять игру.
- `-allow_third_party_software` — нужен ТОЛЬКО для оверлеев (RTSS/Afterburner); отдельная строка `LaunchOptionsWithOverlay`.
- `+fps_max` — НЕ в параметрах запуска: fps_max задаётся в autoexec капом у стабильного потолка
  (среднее по тесту или чуть ниже), НЕ 0 и НЕ 2×герцовки — ровнее фреймтайм.
