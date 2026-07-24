# Source2Boost — Каталог твиков (ядро движка Core)

Каждый твик реализует `ITweak`: `Apply()`, `Revert()`, `IsApplied()`, `RiskLevel`, локализованные `Title`/`Description` (RU/UK/EN), `Category`, `Impact` (ожидаемый эффект), `RequiresRestart`.

**Уровни риска:** `Safe` (обратимо, влияния на стабильность нет) · `Medium` (глубже, но с бэкапом) · `High` (может задеть стабильность/безопасность — только в профиле MAX с явным подтверждением).

**Перед КАЖДЫМ применением:** экспорт затрагиваемых веток реестра в `backups\<timestamp>\` + создание точки восстановления Windows (`Checkpoint-Computer`).

Целевое железо и диагноз — см. память проекта: CPU-bound i7-6700 + медленная DDR3-1866 + 180 Гц. Приоритет: средний FPS ↑ и ровный фреймтайм.

---

## 1. Фреймтайм / латентность (главное против дёрганья)

| Твик | Что делает | Реализация | Риск | Откат |
|---|---|---|---|---|
| Timer Resolution 0.5ms | Держит системный таймер на 0.5мс, пока открыт CS2 → ровнее фреймтайм | `NtSetTimerResolution` (ntdll) фоновым потоком; проверка `GlobalTimerResolutionRequests` | Safe | Отпустить таймер |
| MMCSS / Games priority | Повышает планирование игрового потока | `HKLM\...\Multimedia\SystemProfile\Tasks\Games`: GPU Priority=8, Priority=6, Scheduling Category=High, SFIO Priority=High | Safe | Восстановить дефолт |
| Приоритет + affinity cs2.exe | `High` приоритет процесса, привязка к физ. ядрам | Watchdog находит cs2.exe → `PriorityClass=High`; опц. affinity | Safe | Normal при выходе |
| Отключить Fullscreen Optimizations для cs2.exe | Убирает промежуточный композитор → меньше латентность | `HKCU\...\Layers`: `~ DISABLEDXMAXIMIZEDWINDOWEDMODE` | Safe | Удалить флаг |
| GPU Hardware Scheduling (HAGS) | Тест вкл/выкл — на Skylake иногда лучше ВЫКЛ | `HKLM\...\GraphicsDrivers: HwSchMode` (1/2) | Medium | Вернуть прежнее |
| Game Mode ON | Приоритезация игры Windows | `HKCU\Software\Microsoft\GameBar: AllowAutoGameMode=1` | Safe | Прежнее значение |

## 2. NVIDIA (GTX 1650S) — латентность и производительность

| Твик | Что делает | Реализация | Риск |
|---|---|---|---|
| Low Latency Mode = Ultra | Минимум пре-рендер кадров | NVAPI / профиль драйвера для cs2.exe | Safe |
| Power Management = Prefer Max Performance | Держит буст-частоты, убирает просадки | NVAPI / реестр профиля | Safe |
| Vsync = Off (драйвер) | Управляем капом через fps_max | NVAPI | Safe |
| Threaded Optimization = On | Разгрузка драйвера по потокам | NVAPI | Safe |

> Реализация через NvAPIWrapper либо запись NVIDIA Profile (`nvidiaProfileInspector`-совместимо). Fallback: инструкция в UI, если NVAPI недоступен.

## 3. CPU / планировщик / питание

| Твик | Что делает | Реализация | Риск |
|---|---|---|---|
| Core Parking OFF | Не даёт ядрам засыпать | `powercfg -setacvalueindex ... CPMINCORES 100` + apply | Safe |
| Min processor state 100% | Держит частоту | powercfg PROCTHROTTLEMIN=100 | Safe |
| Ультра-план питания | Клонирует High Performance → максимум | powercfg duplicatescheme + tune | Safe |
| Отключить C-states троттлинг (агрессивно) | Меньше микро-простоев | powercfg атрибуты | High (MAX) |

## 4. Память (DDR3 — узкое место)

| Твик | Что делает | Реализация | Риск |
|---|---|---|---|
| Очистка standby list (как ISLC) | Сбрасывает кэш при заполнении → меньше стоков по памяти | Периодический `NtSetSystemInformation(MemoryPurgeStandbyList)` при пороге | Safe |
| Отключить SysMain/Superfetch | Меньше фонового IO/памяти | `Stop-Service SysMain` + Startup=Disabled | Safe |
| LargeSystemCache / paging tune | Настройка поведения подкачки | `HKLM\...\Memory Management` | Medium |
| ⚠️ Рекомендация (не твик) | Разнокалиберный набор ОЗУ → нет полноценного dual-channel | UI-карточка: собрать парный кит / XMP в BIOS | — |

## 5. Фоновые службы и телеметрия (CPU/IO разгрузка)

| Твик | Что отключаем | Риск |
|---|---|---|
| Телеметрия/диагностика | DiagTrack, dmwappushservice | Safe |
| Поиск | WSearch (если не нужен) | Medium |
| Печать | Spooler (если нет принтера) | Medium |
| Xbox DVR / Game Bar запись | GameDVR (`AppCaptureEnabled=0`, `GameDVR_Enabled=0`) | Safe |
| Прочий bloat | MapsBroker, RetailDemo, Fax | Safe |

> Все службы — с сохранением исходного `StartType` для точного отката.

## 6. Сеть / пинг

| Твик | Что делает | Реализация | Риск |
|---|---|---|---|
| Отключить Nagle | Меньше задержка мелких пакетов | реестр интерфейса: `TcpAckFrequency=1`, `TCPNoDelay=1` | Safe |
| UDP буферы CS2 | Больше `SO_RCVBUF/SNDBUF` | системные параметры/QoS | Safe |
| QoS DSCP для cs2.exe | Приоритет трафика игры | политика QoS порт 27015/27005 | Safe |
| Network throttling off | Убирает сетевой троттлинг мультимедиа | `HKLM\...\Multimedia\SystemProfile: NetworkThrottlingIndex=0xffffffff` | Safe |
| Выбор DNS | Cloudflare/Google | netsh | Safe |

## 7. Конфиг CS2 (модуль Cs2)

- **launch options** (без мифов, только рабочее): `-high -novid -nojoy +fps_max 0 +engine_no_focus_sleep 0` (подбор под железо), тюнинг потоков.
- **autoexec.cfg**: `fps_max` (0 либо 400 — тест), rates под соединение (`cl_updaterate`, `rate`), звук, отключение лишних эффектов, HUD, `cl_disable_ragdolls`, сетевые cvars, интерполяция.
- Запись в `...\game\csgo\cfg\autoexec.cfg` + `+exec autoexec` в launch options. Бэкап предыдущего.

---

## Профили

- **Safe** — только `Safe`-твики. Прирост скромный, риск ≈ ноль. Для друзей по умолчанию.
- **MAX** — Safe + Medium + High. Максимум выжимания (выбор пользователя). Всё равно с точкой восстановления.
- **Benchmark** — временный максимум для замера, авто-откат после теста.

Каждый твик показывается в продвинутом чек-листе с галочкой, риском и ожидаемым эффектом; режим «в один клик» применяет весь выбранный профиль.
