---
name: release
description: >-
  Выпуск новой версии Source2Boost — пересборка установщика и публикация релиза на GitHub.
  Используй ВСЕГДА, когда пользователь просит «выпустить релиз», «залить новую версию»,
  «собрать установщик и опубликовать», «cut a release», «обновить версию на GitHub» или
  поднять версию приложения. Покрывает весь цикл: бамп версии → сборка обоих конфигов →
  self-contained publish → установщик Inno Setup → SHA256 → git-коммит → gh release →
  обновление update.json для авто-апдейтера.
---

# Релиз Source2Boost

Выпуск версии — это сборка self-contained установщика и публикация его как GitHub Release,
плюс обновление `update.json`, по которому встроенный авто-апдейтер предлагает обновление
пользователям. Порядок шагов важен: **`update.json` бампится ТОЛЬКО после того, как ассет
релиза реально загружен** — иначе апдейтер у людей будет ловить 404.

Всё общение с пользователем — на русском (правило проекта из `CLAUDE.md`).

## Инструменты и пути (Windows)

- dotnet: `C:\Program Files\dotnet\dotnet.exe`
- Inno Setup: `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`
- Установщик на выходе: `dist\Source2Boost-Setup.exe`
- Личность коммитов: автор `Source2Boost <noreply@source2boost.local>`,
  **без** `Co-Authored-By` и любой AI-атрибуции (правило проекта).

## Шаг 1. Определить версию

Выбери новую версию (SemVer). Прошлую публичную смотри в `update.json` (`version`) и в
`gh release list`. Забампь её В ДВУХ местах — иначе установщик и приложение разойдутся:

- `src/Source2Boost.App/Source2Boost.App.csproj` → `<Version>X.Y.Z</Version>`
- `installer/Source2Boost.iss` → `#define MyAppVersion "X.Y.Z"`

## Шаг 2. Собрать артефакты

Запусти bundled-скрипт — он делает всё детерминированное: убивает запущенное приложение
(оно лочит `Source2Boost.Core.dll`), собирает **оба** конфига для sanity-check, чистит
`publish/`, публикует self-contained x64 и собирает установщик, затем печатает SHA256 и размер.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .claude\skills\release\scripts\build-release.ps1
```

В конце скрипт выводит блок `RELEASE ARTIFACT` с путём, размером и **SHA256** — сохрани хеш,
он нужен для `update.json` и заметок релиза. Если скрипт упал на сборке — чини причину, не
продолжай (битый установщик хуже отсутствующего).

Почему оба конфига: ярлык пользователя указывает на `bin\Debug`, поэтому Debug тоже должен
быть валиден; Release идёт в установщик. Скрипт собирает оба как проверку, что ничего не
сломано, до долгого publish.

## Шаг 3. Обновить описания (по необходимости)

Пробегись по `README.md` и заметкам: убери устаревшее, добавь новые фичи. Сверяйся С КОДОМ,
а не с памятью — формулировки в README не раз расходились с реальным поведением (например
«живой график PresentMon» при живом полу-авто бенчмарке; «очистка standby каждые 5 мин» при
суточной задаче). Не уверен в факте — проверь grep’ом по `src/`.

## Шаг 4. Закоммитить изменения версии/описаний

Коммить bump версии и правки README. Личность — как указано выше, без соавтора:

```bash
git -c user.name="Source2Boost" -c user.email="noreply@source2boost.local" \
  commit -aF <файл-с-сообщением> --author="Source2Boost <noreply@source2boost.local>"
```

Многострочное сообщение пиши в файл и передавай через `-F` (PowerShell here-string ломается на
кавычках). `update.json` пока НЕ коммить — он бампится в шаге 6.

## Шаг 5. Создать GitHub-релиз с установщиком

Проверь `gh auth status` (аккаунт `biohacker228`). Затем:

```bash
gh release create vX.Y.Z "dist/Source2Boost-Setup.exe" \
  --title "Source2Boost X.Y.Z" --notes-file <заметки.md> --target main --latest
```

**Сеть на этой машине нестабильна** — заливка ~51 МБ ассета регулярно ловит таймаут, и релиз
остаётся черновиком без ассета. Это нормально, не пересоздавай релиз. Вместо этого:

```bash
# дозалить ассет (таймаут ставь большой, до 10 мин):
gh release upload vX.Y.Z "dist/Source2Boost-Setup.exe" --clobber
# опубликовать черновик и пометить latest:
gh release edit vX.Y.Z --draft=false --latest
# проверить (при EOF/таймауте — повтори несколько раз):
gh release view vX.Y.Z --json isDraft,assets --jq '{draft:.isDraft, dl:.assets[0].url, state:.assets[0].state}'
```

Убедись, что `draft:false`, `state:"uploaded"`, а `dl` совпадает с URL, который пойдёт в
`update.json`.

## Шаг 6. Обновить update.json и запушить

Только теперь, когда ассет живой, обнови `update.json`:

- `version` → `X.Y.Z`
- `url` → download-URL ассета (`.../releases/download/vX.Y.Z/Source2Boost-Setup.exe`)
- `sha256` → хеш из шага 2 (верхний регистр, как раньше)
- `notes` → краткий список изменений (в JSON переносы строк как `\n`)

Закоммить `update.json` (та же личность) и `git push`. Готово: апдейтер у пользователей
прошлой версии теперь предложит обновление.

## Шаг 7. Память

Если в `memory/` была заметка «pending release» для этой версии — удали её (релиз состоялся)
и почисти строку в `memory/MEMORY.md`.

## Чек-лист

- [ ] Версия забамплена в `.csproj` И `.iss`
- [ ] `build-release.ps1` отработал, SHA256 сохранён
- [ ] README/заметки актуальны (сверено с кодом)
- [ ] Коммит версии/README (автор Source2Boost, без соавтора)
- [ ] Релиз создан, ассет `uploaded`, `draft:false`, `latest`
- [ ] `update.json` обновлён ПОСЛЕ заливки ассета, запушен
- [ ] «pending»-память удалена
