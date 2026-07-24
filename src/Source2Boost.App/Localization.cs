namespace Source2Boost.App;

/// <summary>Сервис локализации RU/UK/EN с переключением на лету.</summary>
public static class Loc
{
    public static string Lang { get; private set; } = "ru";
    public static event Action? Changed;

    public static void Set(string lang)
    {
        if (lang == Lang) return;
        Lang = lang;
        Changed?.Invoke();
    }

    public static string T(string key) => _s.TryGetValue(key, out var v) ? v.For(Lang) : key;

    /// <summary>Славянское склонение по числу: one=1, few=2–4, many=5–0/11–14. En — one/many.</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        if (Lang == "en") return n == 1 ? one : many;
        int nn = Math.Abs(n) % 100, n1 = nn % 10;
        if (nn is > 10 and < 20) return many;
        if (n1 == 1) return one;
        if (n1 is > 1 and < 5) return few;
        return many;
    }

    /// <summary>«21 твик» / «22 твика» / «25 твиков» (и UK/EN) — с правильным склонением.</summary>
    public static string Tweaks(int n)
    {
        var word = Lang switch
        {
            "uk" => Plural(n, "твік", "твіки", "твіків"),
            "en" => n == 1 ? "tweak" : "tweaks",
            _ => Plural(n, "твик", "твика", "твиков"),
        };
        return $"{n} {word}";
    }

    private readonly record struct S(string Ru, string Uk, string En)
    {
        public string For(string l) => l switch { "uk" => Uk, "en" => En, _ => Ru };
    }

    private static readonly Dictionary<string, S> _s = new()
    {
        ["brand.sub"]     = new("Оптимизатор CS2", "Оптимізатор CS2", "CS2 optimizer"),
        ["grp.main"]      = new("ОСНОВНОЕ", "ОСНОВНЕ", "MAIN"),
        ["grp.game"]      = new("ИГРА", "ГРА", "GAME"),
        ["nav.dash"]      = new("Обзор", "Огляд", "Overview"),
        ["nav.boost"]     = new("Оптимизация", "Оптимізація", "Optimize"),
        ["nav.tweaks"]    = new("Твики", "Твіки", "Tweaks"),
        ["nav.monitor"]   = new("Тест и прогноз", "Тест і прогноз", "Test & forecast"),
        ["nav.cs2"]       = new("Настройки CS2", "Налаштування CS2", "CS2 config"),
        ["nav.restore"]   = new("Откат", "Відкат", "Restore"),

        ["dash.title"]    = new("Обзор системы", "Огляд системи", "System overview"),
        ["dash.tagline"]  = new("Железо просканировано", "Залізо проскановано", "Hardware scanned"),
        ["dash.diag"]     = new("Диагноз", "Діагноз", "Diagnosis"),
        ["dash.optimize"] = new("Оптимизировать", "Оптимізувати", "Optimize"),
        ["dash.scan"]     = new("Сканировать заново", "Сканувати знову", "Re-scan"),

        ["spec.cpu"]      = new("Процессор", "Процесор", "CPU"),
        ["spec.gpu"]      = new("Видеокарта", "Відеокарта", "GPU"),
        ["spec.ram"]      = new("Память", "Пам'ять", "RAM"),
        ["spec.mon"]      = new("Монитор", "Монітор", "Monitor"),
        ["note.cpu.limit"]= new("главный лимит", "головний ліміт", "main limit"),
        ["note.gpu.ok"]   = new("есть запас", "є запас", "headroom left"),
        ["note.ram.mixed"]= new("разнокалиберный набор — узкое место", "різнокаліберний набір — вузьке місце", "mismatched kit — bottleneck"),
        ["note.ram.ok"]   = new("норма", "норма", "ok"),
        ["note.ram.single"]= new("одноканал — узкое место", "одноканал — вузьке місце", "single-channel — bottleneck"),
        ["note.gpu.igpu"] = new("встройка — потолок FPS", "вбудована — стеля FPS", "iGPU — FPS ceiling"),
        ["note.mon.high"] = new("высокая частота — хорошо для CS2", "висока частота — добре для CS2", "high refresh — great for CS2"),
        ["note.mon.mid"]  = new("средняя частота", "середня частота", "mid refresh rate"),
        ["note.mon.low"]  = new("низкая частота — потолок плавности", "низька частота — стеля плавності", "low refresh — smoothness ceiling"),
        ["ram.single"]    = new("1 планка", "1 планка", "1 stick"),
        ["ram.dual"]      = new("двухканал", "двоканал", "dual-channel"),
        ["findings.hdr"]  = new("Что можно улучшить", "Що можна покращити", "What you can improve"),

        ["boostmode.title"] = new("Игровой фокус", "Ігровий фокус", "Game Focus"),
        ["boostmode.desc"]  = new("Замораживает лишний фон (браузер, облачные синхронизации, апдейтеры) и чистит память на время игры. Голосовые чаты (Discord и т.п.), музыку и Steam НЕ трогает. Освобождает ядра CPU — выше 1% low, меньше стуттера. Всё вернётся, когда выключишь.",
                                  "Заморожує зайвий фон (браузер, хмарні синхронізації, апдейтери) і чистить пам'ять на час гри. Голосові чати (Discord тощо), музику та Steam НЕ чіпає. Звільняє ядра CPU — вищий 1% low, менше стуттеру. Усе повернеться, коли вимкнеш.",
                                  "Freezes needless background apps (browser, cloud sync, updaters) and frees memory while you play. Leaves voice chats (Discord etc.), music and Steam alone. Frees CPU cores — higher 1% lows, less stutter. Everything comes back when you turn it off."),
        ["boostmode.on"]    = new("Фон усыплён: {0} прил. Ядра и память освобождены.", "Фон приспано: {0} дод. Ядра і пам'ять звільнені.", "Background suspended: {0} apps. Cores and memory freed."),
        ["boostmode.off"]   = new("Фон разбужен ({0} прил.). Всё вернулось к обычному режиму.", "Фон розбуджено ({0} дод.). Усе повернулось.", "Background resumed ({0} apps). Back to normal."),
        ["boostmode.none"]  = new("Подходящих фоновых приложений не найдено — усыплять нечего.", "Відповідних фонових додатків не знайдено.", "No matching background apps found — nothing to suspend."),
        ["boostmode.active"] = new("Режим активен: фон усыплён. Выключи, чтобы разбудить.", "Режим активний: фон приспано. Вимкни, щоб розбудити.", "Active: background suspended. Turn off to resume."),

        ["nav.settings"]     = new("Настройки", "Налаштування", "Settings"),
        ["settings.title"]   = new("Настройки", "Налаштування", "Settings"),
        ["settings.sub"]     = new("Автоматизация: пусть Source2Boost сам держит оптимизацию и включает игровой режим при старте CS2.",
                                   "Автоматизація: хай Source2Boost сам тримає оптимізацію й вмикає ігровий режим при старті CS2.",
                                   "Automation: let Source2Boost keep the optimization enforced and switch on game mode when CS2 starts."),
        ["auto.start.title"] = new("Запускать с Windows", "Запускати з Windows", "Start with Windows"),
        ["auto.start.desc"]  = new("Тихо стартует при входе (свёрнутым), чтобы следить за запуском CS2.",
                                   "Тихо стартує при вході (згорнутим), щоб стежити за запуском CS2.",
                                   "Starts quietly at login (minimized) to watch for CS2 launching."),
        ["auto.start.err"]   = new("Не удалось изменить автозапуск (нужны права администратора).", "Не вдалося змінити автозапуск.", "Couldn't change autostart (admin rights needed)."),
        ["auto.game.title"]  = new("Авто-режим при старте CS2", "Авто-режим при старті CS2", "Auto mode when CS2 starts"),
        ["auto.game.desc"]   = new("Когда запускаешь CS2: включает Игровой фокус и заново применяет твики, которые Windows могла сбросить (службы). После выхода из игры всё возвращает.",
                                   "Коли запускаєш CS2: вмикає Ігровий фокус і заново застосовує твіки, які Windows могла скинути (служби). Після виходу з гри все повертає.",
                                   "When you launch CS2: enables Game Focus and re-applies tweaks Windows may have reset (services). Reverts everything after you quit."),

        ["tweaks.revertall.confirm"] = new("Откатить все применённые твики и вернуть настройки Windows как было? Это отменит всю оптимизацию.",
                                   "Відкотити всі застосовані твіки й повернути налаштування Windows як було? Це скасує всю оптимізацію.",
                                   "Revert every applied tweak and restore the original Windows settings? This undoes all optimization."),
        ["dlg.yes"]          = new("Да", "Так", "Yes"),
        ["dlg.no"]           = new("Отмена", "Скасувати", "Cancel"),
        ["error.title"]      = new("Что-то пошло не так", "Щось пішло не так", "Something went wrong"),
        ["error.body"]       = new("Код ошибки: {0}\n\nПришли этот код разработчику — по нему в журнале найдутся все подробности.\n\nЧто произошло: {1}\n\nЖурнал: {2}",
                                   "Код помилки: {0}\n\nНадішли цей код розробнику — за ним у журналі знайдуться всі подробиці.\n\nЩо сталося: {1}\n\nЖурнал: {2}",
                                   "Error code: {0}\n\nSend this code to the developer — it locates the full details in the log.\n\nWhat happened: {1}\n\nLog: {2}"),
        ["error.crash"]      = new("Source2Boost столкнулся с ошибкой, но продолжит работу.\n\nКод ошибки: {0}\n\nПришли этот код разработчику — по нему в журнале найдутся подробности. Если приложение ведёт себя странно, перезапусти его.\n\nЖурнал: {1}",
                                   "Source2Boost зіткнувся з помилкою, але продовжить роботу.\n\nКод помилки: {0}\n\nНадішли цей код розробнику — за ним у журналі знайдуться подробиці. Якщо застосунок поводиться дивно, перезапусти його.\n\nЖурнал: {1}",
                                   "Source2Boost hit an error but will keep running.\n\nError code: {0}\n\nSend this code to the developer — it locates the details in the log. If the app behaves oddly, restart it.\n\nLog: {1}"),

        ["update.title"]     = new("Обновления", "Оновлення", "Updates"),
        ["update.version"]   = new("Версия", "Версія", "Version"),
        ["update.hint"]      = new("Нажми «Проверить», чтобы узнать, есть ли новая версия.",
                                   "Натисни «Перевірити», щоб дізнатися, чи є нова версія.",
                                   "Click «Check» to see if a new version is available."),
        ["update.check"]     = new("Проверить", "Перевірити", "Check"),
        ["update.checking"]  = new("Проверяю обновления…", "Перевіряю оновлення…", "Checking for updates…"),
        ["update.uptodate"]  = new("У тебя последняя версия.", "У тебе остання версія.", "You're on the latest version."),
        ["update.available"] = new("Доступна версия {0}.", "Доступна версія {0}.", "Version {0} is available."),
        ["update.confirm"]   = new("Скачать и установить версию {0}? Приложение закроется, чтобы установщик обновил файлы.",
                                   "Завантажити й встановити версію {0}? Застосунок закриється, щоб інсталятор оновив файли.",
                                   "Download and install version {0}? The app will close so the installer can update files."),
        ["update.downloading"] = new("Скачиваю… {0}%", "Завантажую… {0}%", "Downloading… {0}%"),
        ["update.error"]     = new("Не удалось проверить обновления (проверь интернет).",
                                   "Не вдалося перевірити оновлення (перевір інтернет).",
                                   "Couldn't check for updates (check your connection)."),
        ["welcome.sub"]   = new("Начнём со сканирования системы: найдём слабые места, подберём план оптимизации и покажем, что стоит сделать вручную.",
                                "Почнемо зі сканування системи: знайдемо слабкі місця, підберемо план оптимізації та покажемо, що варто зробити вручну.",
                                "Let's start by scanning your system: we'll find weak spots, pick an optimization plan and show what's worth doing manually."),
        ["welcome.scan"]  = new("Сканировать систему", "Сканувати систему", "Scan system"),
        ["welcome.scanning"] = new("Сканирую систему…", "Скануюю систему…", "Scanning system…"),
        ["loading"]       = new("Загрузка…", "Завантаження…", "Loading…"),

        ["tweaks.title"]  = new("Твики вручную", "Твіки вручну", "Manual tweaks"),
        ["tweaks.sub"]    = new("Включай по одному. У каждого — риск и ожидаемый эффект.",
                                "Вмикай по одному. У кожного — ризик та очікуваний ефект.",
                                "Toggle them one by one. Each shows risk and expected impact."),

        ["boost.title"]   = new("Оптимизация в один клик", "Оптимізація в один клік", "One-click optimization"),
        ["boost.stub"]    = new("Скоро: профили Safe / Максимум / Бенчмарк.", "Скоро: профілі Safe / Максимум / Бенчмарк.", "Coming soon: Safe / Maximum / Benchmark profiles."),
        ["boost.sub"]     = new("Выбери профиль. Всё обратимо, перед применением создаётся точка восстановления.",
                                "Обери профіль. Усе оборотно, перед застосуванням створюється точка відновлення.",
                                "Pick a profile. Everything is reversible; a restore point is created first."),
        ["profile.safe"]    = new("Безопасный", "Безпечний", "Safe"),
        ["profile.optimal"] = new("Оптимальный", "Оптимальний", "Optimal"),
        ["profile.max"]     = new("Максимум", "Максимум", "Maximum"),
        ["profile.recommended"] = new("рекомендуется", "рекомендується", "recommended"),
        ["profile.apply"]   = new("Применить", "Застосувати", "Apply"),
        ["profile.safe.desc"]    = new("Только безопасные твики (риск 0). Без перезагрузки, мгновенно обратимо. {0}.",
                                       "Лише безпечні твіки (ризик 0). Без перезавантаження, миттєво оборотно. {0}.",
                                       "Safe tweaks only (zero risk). No reboot, instantly reversible. {0}."),
        ["profile.optimal.desc"] = new("Баланс прироста и безопасности: безопасные + средние. Подходит большинству. {0}.",
                                       "Баланс приросту й безпеки: безпечні + середні. Підходить більшості. {0}.",
                                       "Balance of gains and safety: safe + medium. Best for most. {0}."),
        ["profile.max.desc"]     = new("Всё, включая агрессивное (Spectre off, таймеры загрузчика, Nagle). Максимум FPS, снижаются часть защит. {0}.",
                                       "Усе, включно з агресивним (Spectre off, таймери завантажувача, Nagle). Максимум FPS, знижуються частина захистів. {0}.",
                                       "Everything, including aggressive (Spectre off, boot timers, Nagle). Max FPS, lowers some protections. {0}."),
        ["monitor.title"] = new("Мониторинг фреймтайма", "Моніторинг фреймтайму", "Frametime monitor"),
        ["monitor.stub"]  = new("Скоро: живой график FPS/фреймтайма через PresentMon.", "Скоро: живий графік FPS/фреймтайму через PresentMon.", "Coming soon: live FPS/frametime graph via PresentMon."),
        ["cs2.title"]     = new("Настройки CS2", "Налаштування CS2", "CS2 config"),
        ["cs2.stub"]      = new("Скоро: генерация autoexec.cfg и параметров запуска.", "Скоро: генерація autoexec.cfg та параметрів запуску.", "Coming soon: autoexec.cfg and launch options."),

        ["monitor.sub"]     = new("Замер реального фреймтайма cs2.exe через PresentMon.", "Замір реального фреймтайму cs2.exe через PresentMon.", "Measure cs2.exe real frametime via PresentMon."),
        ["monitor.hint"]    = new("Запусти CS2, зайди на карту с ботами, затем жми «Замерить». Замер идёт 60 секунд — активно двигайся и стреляй.",
                                  "Запусти CS2, зайди на карту з ботами, потім тисни «Заміряти». Замір триває 60 секунд — активно рухайся та стріляй.",
                                  "Launch CS2, load a map with bots, then hit Measure. It runs 60 seconds — move and shoot actively."),
        ["monitor.measure"] = new("Замерить (60 сек)", "Заміряти (60 с)", "Measure (60s)"),
        ["monitor.working"] = new("Замеряю 60 секунд… играй активно.", "Замірюю 60 секунд… грай активно.", "Measuring 60s… play actively."),
        ["monitor.nocs2"]   = new("Сначала запусти CS2 и зайди на карту, потом жми «Замерить».", "Спершу запусти CS2 і зайди на карту, потім тисни «Заміряти».", "Launch CS2 and load a map first, then hit Measure."),
        ["monitor.countdown"] = new("Замеряю… осталось {0} сек. Играй активно (двигайся, стреляй).", "Замірюю… залишилось {0} с. Грай активно (рухайся, стріляй).", "Measuring… {0}s left. Play actively (move, shoot)."),
        ["monitor.result"]  = new("Средний FPS: {0}\n1% low: {1}\n0.1% low: {2}\nМакс. стуттер: {3} мс\nРазброс фреймтайма: {4}\nКадров: {5}",
                                  "Середній FPS: {0}\n1% low: {1}\n0.1% low: {2}\nМакс. стуттер: {3} мс\nРозкид фреймтайму: {4}\nКадрів: {5}",
                                  "Average FPS: {0}\n1% low: {1}\n0.1% low: {2}\nMax stutter: {3} ms\nFrametime spread: {4}\nFrames: {5}"),

        ["cs2.sub"]         = new("Параметры запуска Steam и autoexec.cfg для максимума FPS.", "Параметри запуску Steam та autoexec.cfg для максимуму FPS.", "Steam launch options and autoexec.cfg for max FPS."),
        ["cs2.path"]        = new("Установка CS2", "Встановлення CS2", "CS2 install"),
        ["cs2.launch"]      = new("Параметры запуска", "Параметри запуску", "Launch options"),
        ["cs2.copy"]        = new("Копировать", "Копіювати", "Copy"),
        ["cs2.copied"]      = new("Скопировано ✓", "Скопійовано ✓", "Copied ✓"),
        ["cs2.instr"]       = new("Вставь в Steam: ПКМ по CS2 → Свойства → Параметры запуска.", "Встав у Steam: ПКМ по CS2 → Властивості → Параметри запуску.", "Paste in Steam: right-click CS2 → Properties → Launch Options."),
        ["cs2.autoexec"]    = new("autoexec.cfg", "autoexec.cfg", "autoexec.cfg"),
        ["cs2.install"]     = new("Установить autoexec.cfg", "Встановити autoexec.cfg", "Install autoexec.cfg"),
        ["cs2.notfound"]    = new("CS2 не найден (установлен ли CS2 и запускался ли Steam?)", "CS2 не знайдено (чи встановлено CS2 і чи запускався Steam?)", "CS2 not found (is it installed and has Steam run?)"),
        ["cs2.installed"]   = new("Установлен: {0}", "Встановлено: {0}", "Installed: {0}"),
        ["cs2.notinstalled"]= new("Не установлен. Нажми кнопку ниже.", "Не встановлено. Натисни кнопку нижче.", "Not installed. Click the button below."),
        ["cs2.install.ok"]  = new("autoexec.cfg записан:\n{0}\n\nПараметры запуска (кнопка «Копировать» выше) уже включают +exec autoexec.",
                                  "autoexec.cfg записано:\n{0}\n\nПараметри запуску (кнопка «Копіювати» вище) вже містять +exec autoexec.",
                                  "autoexec.cfg written:\n{0}\n\nThe launch options (Copy button above) already include +exec autoexec."),
        ["cs2.video.title"] = new("Игровой конфиг графики (макс. FPS)", "Ігровий конфіг графіки (макс. FPS)", "In-game graphics config (max FPS)"),
        ["cs2.video.desc"]  = new("Ставит настройки графики CS2 на competitive-low (тени/эффекты/шейдеры low, MSAA и VSync off) — самый крупный игровой рычаг FPS. Разрешение не трогаем. Полностью обратимо.",
                                  "Ставить налаштування графіки CS2 на competitive-low (тіні/ефекти/шейдери low, MSAA та VSync off) — найбільший ігровий важіль FPS. Роздільність не чіпаємо. Повністю оборотно.",
                                  "Sets CS2 graphics to competitive-low (shadows/effects/shaders low, MSAA and VSync off) — the biggest in-game FPS lever. Resolution untouched. Fully reversible."),
        ["cs2.video.warn"]  = new("⚠ Закрой CS2 перед применением — иначе игра перезапишет изменения при выходе.",
                                  "⚠ Закрий CS2 перед застосуванням — інакше гра перезапише зміни при виході.",
                                  "⚠ Close CS2 before applying — otherwise the game overwrites the changes on exit."),
        ["cs2.video.apply"] = new("Применить макс. FPS", "Застосувати макс. FPS", "Apply max FPS"),
        ["cs2.video.restore"] = new("Вернуть мои настройки", "Повернути мої налаштування", "Restore my settings"),
        ["cs2.video.nofile"] = new("cs2_video.txt не найден — запусти CS2 хотя бы раз, чтобы игра его создала.",
                                   "cs2_video.txt не знайдено — запусти CS2 хоча б раз.",
                                   "cs2_video.txt not found — launch CS2 once so the game creates it."),
        ["cs2.video.applied"] = new("✓ Применён конфиг макс. FPS. Запусти CS2 — настройки уже низкие.",
                                    "✓ Застосовано конфіг макс. FPS. Запусти CS2.",
                                    "✓ Max-FPS config applied. Launch CS2 — settings are already low."),
        ["cs2.video.ok"]    = new("Готово! Настройки графики CS2 выставлены на макс. FPS. Запускай игру.\n\nНе понравится — жми «Вернуть мои настройки».",
                                  "Готово! Налаштування графіки CS2 виставлені на макс. FPS. Запускай гру.\n\nНе сподобається — тисни «Повернути мої налаштування».",
                                  "Done! CS2 graphics set to max FPS. Launch the game.\n\nDon't like it? Hit 'Restore my settings'."),
        ["cs2.video.restored"] = new("Твои прежние настройки графики восстановлены.", "Твої попередні налаштування графіки відновлено.", "Your previous graphics settings are restored."),
        ["cs2.video.err.running"] = new("Сначала полностью закрой CS2 — она перезапишет файл при выходе.",
                                        "Спершу повністю закрий CS2 — вона перезапише файл при виході.",
                                        "Close CS2 completely first — it overwrites the file on exit."),
        ["cs2.video.err.nofile"] = new("cs2_video.txt не найден. Запусти CS2 хотя бы раз.", "cs2_video.txt не знайдено. Запусти CS2 хоча б раз.", "cs2_video.txt not found. Launch CS2 once."),
        ["cs2.video.err.generic"] = new("Не удалось изменить конфиг графики. Подробности в логе.", "Не вдалося змінити конфіг графіки. Деталі в лозі.", "Couldn't change the graphics config. See the log."),

        ["restore.title"] = new("Откат изменений", "Відкат змін", "Restore changes"),
        ["restore.stub"]  = new("Скоро: список снимков и откат в один клик.", "Скоро: список знімків і відкат в один клік.", "Coming soon: snapshots list and one-click revert."),
        ["restore.sub"]   = new("Применённые твики. Откати любой по отдельности или все сразу.", "Застосовані твіки. Відкоти будь-який окремо або всі одразу.", "Applied tweaks. Revert any one, or all at once."),
        ["restore.empty"] = new("Пока ничего не применено.", "Поки нічого не застосовано.", "Nothing applied yet."),
        ["restore.refresh"] = new("Обновить", "Оновити", "Refresh"),
        ["restore.revert"] = new("Откатить", "Відкотити", "Revert"),
        ["restore.reboot"] = new("· нужна перезагрузка", "· потрібне перезавантаження", "· needs reboot"),
        ["restore.note"]  = new("Бэкапы реестра и точка восстановления Windows создаются автоматически перед изменениями.",
                                "Бекапи реєстру та точка відновлення Windows створюються автоматично перед змінами.",
                                "Registry backups and a Windows restore point are created automatically before changes."),

        ["risk.safe"]     = new("риск 0", "ризик 0", "zero risk"),
        ["risk.medium"]   = new("средний", "середній", "medium"),
        ["risk.high"]     = new("агрессивно", "агресивно", "aggressive"),
        ["risk.extreme"]  = new("безбашенно", "безбашенно", "unhinged"),
        ["risk.soon"]     = new("скоро", "скоро", "soon"),

        ["theme"]         = new("Тема", "Тема", "Theme"),
        ["opt.title"]     = new("Оптимизация", "Оптимізація", "Optimization"),
        ["opt.confirm"]   = new("Применить профиль «{0}» ({1})?\nПеред применением создаётся точка восстановления Windows.",
                                "Застосувати профіль «{0}» ({1})?\nПеред застосуванням створюється точка відновлення Windows.",
                                "Apply the “{0}” profile ({1})?\nA Windows restore point is created first."),
        ["opt.reboot"]    = new("\n⚠ Часть изменений вступит в силу только после перезагрузки.",
                                "\n⚠ Частина змін набуде чинності лише після перезавантаження.",
                                "\n⚠ Some changes take effect only after a restart."),
        ["profile.reboot"] = new("⚠ нужна перезагрузка", "⚠ потрібне перезавантаження", "⚠ needs a restart"),
        ["profile.active"] = new("Активно", "Активно", "Active"),

        ["nav.bios"]      = new("BIOS", "BIOS", "BIOS"),

        // Тест: оценка + прогноз FPS
        ["test.title"]    = new("Тест и прогноз", "Тест і прогноз", "Test & forecast"),
        ["test.sub"]      = new("Оценка оптимизации, прогноз FPS и реальный замер фреймтайма cs2.exe.",
                                "Оцінка оптимізації, прогноз FPS та реальний замір фреймтайму cs2.exe.",
                                "Optimization score, FPS forecast and a real cs2.exe frametime measurement."),
        ["score.title"]   = new("Оценка оптимизации", "Оцінка оптимізації", "Optimization score"),
        ["score.sub"]     = new("Применено {0} из {1}. Чем выше — тем полнее реализован потенциал твиков.",
                                "Застосовано {0} з {1}. Чим вище — тим повніше реалізовано потенціал твіків.",
                                "Applied {0} of {1}. Higher means more of the tweak potential is realized."),
        ["forecast.title"] = new("Прогноз среднего FPS в CS2", "Прогноз середнього FPS у CS2", "CS2 average FPS forecast"),
        ["forecast.line"]  = new("Сейчас ~{0} → потенциал ~{1} (с настройкой BIOS ~{2})",
                                 "Зараз ~{0} → потенціал ~{1} (з налаштуванням BIOS ~{2})",
                                 "Now ~{0} → potential ~{1} (with BIOS tuning ~{2})"),
        ["forecast.measured"] = new("на основе твоего замера", "на основі твого заміру", "based on your measurement"),
        ["forecast.estimate"] = new("грубая оценка по железу — сделай замер для точности",
                                    "груба оцінка за залізом — зроби замір для точності",
                                    "rough hardware estimate — run a measurement for accuracy"),
        ["cs2.fps.label"] = new("Лимит FPS (fps_max в autoexec)", "Ліміт FPS (fps_max в autoexec)", "FPS cap (fps_max in autoexec)"),
        ["cs2.fps.hint"]  = new("Ставь у стабильного потолка = средний FPS после теста или чуть ниже: ровнее фреймтайм. 0 = без лимита. Рекомендуем: {0}.",
                                "Став біля стабільної стелі = середній FPS після тесту або трохи нижче: рівніший фреймтайм. 0 = без ліміту. Рекомендуємо: {0}.",
                                "Set it at your stable ceiling = average FPS after testing or slightly below: steadier frametime. 0 = unlimited. Suggested: {0}."),
        ["bios.title"]    = new("Настройка BIOS/UEFI", "Налаштування BIOS/UEFI", "BIOS/UEFI setup"),
        ["bios.sub"]      = new("Рекомендации под твоё железо. BIOS программа не меняет — эти шаги нужно сделать вручную при загрузке (обычно Del/F2).",
                                "Рекомендації під твоє залізо. BIOS програма не змінює — ці кроки треба зробити вручну під час завантаження (зазвичай Del/F2).",
                                "Recommendations for your hardware. The app can't change BIOS — do these manually at boot (usually Del/F2)."),
        ["bios.level.rec"] = new("рекомендуется", "рекомендується", "recommended"),
        ["bios.level.opt"] = new("по желанию", "за бажанням", "optional"),
        ["bios.level.warn"] = new("важно", "важливо", "important"),
        ["opt.working"]   = new("Применяю…", "Застосовую…", "Applying…"),
        ["opt.done"]      = new("Готово: применено {0} из {1}.\nОткатить можно на вкладке «Откат» или кнопкой «Откатить всё».",
                                "Готово: застосовано {0} з {1}.\nВідкотити можна на вкладці «Відкат» або кнопкою «Відкотити все».",
                                "Done: {0} of {1} applied.\nRevert via the Restore tab or the “Revert all” button."),
        ["tweaks.apply"]  = new("Применить выбранные", "Застосувати вибрані", "Apply selected"),
        ["tweaks.revertall"] = new("Откатить всё", "Відкотити все", "Revert all"),
        ["tweaks.result"] = new("Применено: {0} · Пропущено: {1}", "Застосовано: {0} · Пропущено: {1}", "Applied: {0} · Skipped: {1}"),
        ["tweaks.reverted"] = new("Откат выполнен: {0}", "Відкат виконано: {0}", "Reverted: {0}"),
        ["tweaks.reconcile"] = new("Включено: {0} · Откачено: {1}", "Увімкнено: {0} · Відкочено: {1}", "Enabled: {0} · Reverted: {1}"),
    };
}
