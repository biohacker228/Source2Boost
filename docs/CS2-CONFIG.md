# CS2: launch options и конфиг (модуль Cs2)

Принцип CS2 (в отличие от CSGO): **меньше флагов = лучше**. Движок Source 2 сам управляет потоками и очередями рендера. Многие старые «бустеры» удалены или вредят.

## Launch options — генерируем (валидные для CS2)

```
-novid -console -nojoy -high -fullscreen -freq 180 +fps_max 0 -softparticlesdefaultoff +exec autoexec
```

| Флаг | Зачем | Примечание |
|---|---|---|
| `-novid` | пропуск интро | быстрее старт |
| `-console` | включить консоль | нужно для диагностики/бенча |
| `-nojoy` | отключить геймпад | освобождает ресурсы |
| `-high` | высокий приоритет процесса | *опционально* — у нас есть более безопасный аналог через приоритет Windows; даём тумблером |
| `-fullscreen` | эксклюзивный fullscreen | ниже латентность, чем borderless |
| `-freq 180` | частота под твой монитор | подставляем реальную герцовку |
| `+fps_max 0` | без лимита FPS | приоритет — макс FPS |
| `-softparticlesdefaultoff` | частицы без feathering | небольшой прирост FPS |
| `+exec autoexec` | подгрузить autoexec | обязательно |

## ❌ Мусор из CSGO, который НЕ добавляем (игнор/вред в CS2)

`-d3d9ex`, `-no-browser`, `+mat_queue_mode 2`, `-tickrate 128` (для ММ фейк — sub-tick), `+cl_interp*`, `+cl_cmdrate/updaterate`, `-threads N` (движок сам), `+cl_forcepreload` в лаунче (делаем в autoexec).

## video-настройки (не autoexec!)

Графика в CS2 хранится в `...\game\csgo\cfg\cs2_video.txt` (+ настройки в UI), не в cvar. Модуль Cs2 отдельно предложит профиль под 1080p/GTX1650S:
- Multisample AA: низкий/выкл (CPU-разгрузка на overlay-геометрии — тест)
- Shadow Quality: Low/Medium (шейдеры теней бьют по CPU)
- Model/Texture Detail: под 4ГБ VRAM
- Boost Player Contrast, Vsync **Off** (кап через fps_max)

## Порядок работы модуля Cs2

1. Найти установку CS2: `Steam\steamapps\libraryfolders.vdf` → путь до `Counter-Strike Global Offensive`.
2. Определить SteamID3 пользователя для `userdata\<id>\730\local\cfg`.
3. Бэкап текущего `autoexec.cfg` (если есть) в `backups\`.
4. Записать сгенерированный `autoexec.cfg` в `...\game\csgo\cfg\`.
5. Прописать/предложить launch options (через `localconfig.vdf` при закрытом Steam, либо показать для ручной вставки).
6. Показать в UI, что и зачем сделано (RU/UK/EN).

Плейсхолдеры autoexec: `{{FPS_MAX}}` (0 или 400), `{{RATE}}` (по скорости соединения).
