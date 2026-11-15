using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
namespace VoxDeck.Core;

/// <summary>Tiny runtime-switchable localization (English / Russian).</summary>
public class Loc : INotifyPropertyChanged
{
    public static readonly Loc I = new();
    public string Lang { get; private set; } = "en";
    public event PropertyChangedEventHandler PropertyChanged;
    public event Action Changed;
    public string this[string key] => D.TryGetValue(key, out var v) ? (Lang == "ru" ? v.ru : v.en) : key;
    public static string T(string key) => I[key];
    public void Set(string lang)
    {
        Lang = lang == "ru" ? "ru" : "en";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        Changed?.Invoke();
    }
    public static IEnumerable<string> Keys => D.Keys;

    static readonly Dictionary<string, (string en, string ru)> D = new()
    {
        ["app.tagline"] = ("voice toolkit", "голосовой комбайн"),
        ["nav.tools"] = ("TOOLS", "ИНСТРУМЕНТЫ"), ["nav.app"] = ("APP", "ПРИЛОЖЕНИЕ"),
        ["nav.tts"] = ("Text to Speech", "Текст в речь"), ["nav.pad"] = ("Soundpad", "Саундпад"), ["nav.changer"] = ("Voice Changer", "Войсченджер"), ["nav.settings"] = ("Settings", "Настройки"),
        ["voice.title"] = ("VIRTUAL MIC", "ВИРТ. МИКРОФОН"),
        ["voice.missing"] = ("Not connected", "Не подключён"), ["voice.on"] = ("Connected", "Подключён"),
        ["voice.missing.hint"] = ("Windows needs a driver for a virtual microphone. VoxDeck installs it for you.", "Для виртуального микрофона Windows нужен драйвер. VoxDeck поставит его сам."),
        ["voice.ready.hint"] = ("In Discord / game pick “CABLE Output” as the microphone.", "В Discord / игре выберите микрофон «CABLE Output»."),
        ["voice.get"] = ("Get VB-CABLE", "Скачать VB-CABLE"), ["voice.recheck"] = ("Re-check devices", "Проверить устройства снова"), ["voice.found"] = ("Virtual mic connected", "Виртуальный микрофон подключён"),
        ["hear.sounds"] = ("Hear it myself", "Слышать самому"), ["hear.sounds.tip"] = ("Also play sounds and speech in your own headphones", "Проигрывать звуки и речь также в ваших наушниках"),
        ["stop.all"] = ("Stop all sounds", "Остановить всё"), ["stopped"] = ("Stopped", "Остановлено"),
        ["status.ready"] = ("Ready", "Готово"), ["status.changer"] = ("Voice changer", "Войсченджер"), ["status.mic"] = ("Microphone live", "Микрофон в эфире"),
        ["play"] = ("Play", "Проиграть"), ["preview"] = ("Preview", "Превью"), ["download"] = ("Download", "Скачать"), ["stop"] = ("Stop", "Стоп"), ["clear"] = ("Clear", "Очистить"),
        ["save"] = ("Save", "Сохранить"), ["reset"] = ("Reset", "Сброс"), ["remove"] = ("Remove", "Удалить"), ["cancel"] = ("Cancel", "Отмена"), ["ok"] = ("OK", "ОК"), ["saved"] = ("Saved", "Сохранено"),
        ["off"] = ("off", "выкл"), ["custom"] = ("Custom", "Свой"), ["volume"] = ("Volume", "Громкость"), ["speed"] = ("Speed", "Скорость"), ["chars"] = ("chars", "симв."),
        ["fav"] = ("Favorite", "В избранное"), ["fav.only"] = ("Favorites only", "Только избранные"), ["fav.toggle"] = ("Add / remove favorite", "Добавить / убрать из избранного"),
        ["pin.tip"] = ("Pin: compact always-on-top overlay", "Закрепить: компактное окно поверх всех окон"),
        ["female"] = ("Female", "Женский"), ["male"] = ("Male", "Мужской"), ["gender.all"] = ("Any gender", "Любой пол"),
        ["engine.online"] = ("Neural", "Нейро"), ["engine.system"] = ("Offline", "Офлайн"),
        ["all.languages"] = ("All languages", "Все языки"),
        ["device.default"] = ("System default", "По умолчанию"), ["device.auto"] = ("Auto-detect virtual cable", "Автопоиск виртуального кабеля"), ["device.none"] = ("Disabled", "Отключено"),

        // TTS
        ["tts.title"] = ("Text to Speech", "Текст в речь"),
        ["tts.sub"] = ("Type, pick a voice — speak into the voice chat without a microphone.", "Напишите текст, выберите голос — говорите в голосовом чате без микрофона."),
        ["tts.placeholder"] = ("Type something to say…  (Ctrl+Enter to play)", "Введите текст…  (Ctrl+Enter — проиграть)"),
        ["tts.play.tip"] = ("Speak into the virtual microphone — everyone in the voice chat hears it (Ctrl+Enter)", "Проговорить в виртуальный микрофон — услышат все в голосовом чате (Ctrl+Enter)"),
        ["tts.preview.tip"] = ("Listen only in your headphones", "Послушать только в своих наушниках"),
        ["tts.download.tip"] = ("Save as a WAV file", "Сохранить в WAV-файл"),
        ["tts.settings"] = ("Voice settings", "Настройки голоса"), ["tts.rate"] = ("Speed", "Скорость"), ["tts.pitch"] = ("Pitch", "Тон"), ["tts.volume"] = ("Volume", "Громкость"),
        ["tts.effect"] = ("Voice effect", "Эффект голоса"), ["tts.clearafter"] = ("Clear after sending", "Очищать после отправки"), ["tts.clearafter.sub"] = ("Handy for chatting live", "Удобно для живого общения"),
        ["tts.history"] = ("Recent phrases", "Недавние фразы"), ["tts.voices"] = ("Voices", "Голоса"), ["tts.search"] = ("Search name, language, style…", "Поиск: имя, язык, стиль…"),
        ["voices.count"] = ("{0} of {1}", "{0} из {1}"), ["voice.sample"] = ("Play a sample", "Прослушать образец"),
        ["tts.empty"] = ("Type some text first", "Сначала введите текст"), ["tts.novoice"] = ("Pick a voice", "Выберите голос"), ["tts.error"] = ("Speech failed:", "Ошибка синтеза:"),

        // Soundpad
        ["pad.sub"] = ("Click a pad to play it into the voice chat. Right-click for more.", "Клик — звук уходит в голосовой чат. Правый клик — меню."),
        ["pad.library"] = ("LIBRARY", "БИБЛИОТЕКА"), ["pad.online"] = ("ONLINE", "ОНЛАЙН"), ["pad.builtin"] = ("BUILT-IN", "ВСТРОЕННЫЕ"),
        ["pad.all"] = ("All sounds", "Все звуки"), ["pad.fav"] = ("Favorites", "Избранное"), ["pad.recent"] = ("Recent", "Недавние"), ["pad.top"] = ("Most played", "Самые популярные"), ["pad.mine"] = ("My sounds", "Мои звуки"),
        ["tts.reset"] = ("Reset", "Сбросить"), ["tts.reset.tip"] = ("Back to this voice's normal speed, pitch and volume", "Вернуть обычные скорость, тон и громкость этого голоса"),
        ["tts.clarity"] = ("Clear speech", "Чёткая речь"), ["tts.clarity.sub"] = ("Sharper, evenly loud voice", "Разборчивее и ровнее по громкости"),
        ["tts.clarity.tip"] = ("Processed on your PC: removes rumble, lifts consonants, restores the highs lost in the stream and evens out loudness.", "Обработка на вашем ПК: убирает гул, подчёркивает согласные, возвращает срезанные верха и выравнивает громкость."),
        ["tts.balance"] = ("Even speed", "Ровная скорость"), ["tts.balance.sub"] = ("Slows down voices that rush", "Замедляет слишком быстрые голоса"),
        ["tts.balance.tip"] = ("Some voices speak up to 40% faster than others in the same language. Each was measured and is brought back to a normal pace.", "Некоторые голоса говорят до 40% быстрее остальных на том же языке. Каждый измерен и приведён к нормальному темпу."),
        ["drv.install"] = ("Install driver", "Установить драйвер"), ["drv.tip"] = ("One click: VoxDeck downloads the signed VB-CABLE driver from vb-audio.com, checks its signature and installs it. Windows will ask for permission once.", "Один клик: VoxDeck скачает подписанный драйвер VB-CABLE с vb-audio.com, проверит подпись и установит его. Windows один раз спросит разрешение."),
        ["drv.step.download"] = ("Downloading the driver…", "Скачиваю драйвер…"), ["drv.step.verify"] = ("Checking the signature…", "Проверяю подпись…"), ["drv.step.install"] = ("Confirm the Windows prompt…", "Подтвердите запрос Windows…"),
        ["drv.step.wait"] = ("Waiting for the device…", "Жду появления устройства…"), ["drv.step.done"] = ("Done", "Готово"),
        ["drv.ok"] = ("Virtual microphone installed", "Виртуальный микрофон установлен"), ["drv.cancelled"] = ("Installation cancelled", "Установка отменена"), ["drv.removed"] = ("Driver removed", "Драйвер удалён"),
        ["drv.restart"] = ("Driver installed — restart Windows to finish.", "Драйвер установлен — перезагрузите Windows, чтобы закончить."), ["drv.fail"] = ("Driver was not installed:", "Драйвер не установлен:"),
        ["drv.remove"] = ("Remove the virtual mic driver", "Удалить драйвер виртуального микрофона"),
        ["sysmic.title"] = ("System microphone", "Микрофон системы"), ["sysmic.on"] = ("VoxDeck is now your microphone", "VoxDeck теперь ваш микрофон"),
        ["sysmic.tip"] = ("While VoxDeck is open it becomes the default Windows microphone: Discord and games pick it up by themselves, and your real microphone keeps passing through. Closing VoxDeck gives your microphone back.", "Пока VoxDeck открыт, он становится микрофоном Windows по умолчанию: Discord и игры подхватывают его сами, а ваш настоящий микрофон продолжает проходить насквозь. Закроете VoxDeck — ваш микрофон вернётся."),
        ["voice.system.hint"] = ("Apps that use the default microphone hear VoxDeck — nothing to set up.", "Программы с микрофоном «по умолчанию» уже слышат VoxDeck — настраивать ничего не нужно."),
        ["pad.memes"] = ("Meme buttons", "Мемные кнопки"), ["pad.saved"] = ("Downloaded", "Скачанные"), ["pad.more"] = ("Load more", "Показать ещё"),
        ["pad.search.memes"] = ("Search meme sounds…", "Найти мемный звук…"),
        ["meme.trending"] = ("Trending", "В тренде"), ["meme.best"] = ("All-time best", "Лучшее за всё время"), ["meme.recent"] = ("New", "Новые"), ["site.all"] = ("All sites", "Все сайты"),
        ["pad.charts"] = ("Top charts", "Топ-чарты"), ["pad.songs"] = ("Song search", "Поиск песен"), ["pad.sounds"] = ("Search all sites", "Поиск по всем сайтам"),
        ["genre.all"] = ("All genres", "Все жанры"),
        ["pad.search"] = ("Search sounds…", "Поиск звуков…"), ["pad.search.songs"] = ("Search any song or artist…", "Найти любую песню или исполнителя…"), ["pad.search.sounds"] = ("Search 6 sound sites at once…", "Поиск сразу по 6 сайтам со звуками…"),
        ["pad.sounds.hint"] = ("One search across Myinstants, BBC Sound Effects, Freesound, Mixkit, Wikimedia and Openverse. Type a word (English works best) or pick a tag above.", "Один поиск сразу по Myinstants, BBC Sound Effects, Freesound, Mixkit, Wikimedia и Openverse. Введите слово (лучше на английском) или выберите тег выше."),
        ["pad.empty"] = ("Nothing found", "Ничего не найдено"), ["pad.empty.fav"] = ("No favorites yet — hover a pad and press the star.", "Пока пусто — наведите на пэд и нажмите звёздочку."),
        ["pad.empty.recent"] = ("Play something and it will show up here.", "Проиграйте что-нибудь — и оно появится здесь."),
        ["pad.empty.mine"] = ("Add your own files: press “Add files” or drag && drop MP3 / WAV / OGG / FLAC here.", "Добавьте свои файлы: «Добавить файлы» или перетащите сюда MP3 / WAV / OGG / FLAC."),
        ["pad.empty.online"] = ("Nothing found — try another query.", "Ничего не найдено — попробуйте другой запрос."), ["pad.offline"] = ("Could not reach the online library. Check your internet connection.", "Не удалось подключиться к онлайн-библиотеке. Проверьте интернет."),
        ["pad.count"] = ("{0} sounds", "Звуков: {0}"), ["pad.loaderror"] = ("Could not load the sound:", "Не удалось загрузить звук:"), ["pad.added"] = ("Added: {0}", "Добавлено: {0}"),
        ["pad.add"] = ("Add files", "Добавить файлы"), ["pad.add.tip"] = ("Import your own audio files (or drag && drop them onto the window)", "Импорт своих аудиофайлов (или перетащите их в окно)"),
        ["pad.preview.tip"] = ("Preview (only you hear it)", "Превью (слышите только вы)"), ["pad.overlap.tip"] = ("Overlap: let sounds play on top of each other", "Наложение: звуки играют поверх друг друга"), ["pad.loop.tip"] = ("Loop", "Зациклить"),
        ["sort.default"] = ("Default order", "По умолчанию"), ["sort.name"] = ("Name A–Z", "Название А–Я"), ["sort.popular"] = ("Most popular", "Популярные"), ["sort.short"] = ("Shortest first", "Сначала короткие"), ["sort.long"] = ("Longest first", "Сначала длинные"), ["sort.new"] = ("Recently added", "Недавно добавленные"),
        ["hotkey.assign"] = ("Assign hotkey…", "Назначить горячую клавишу…"),
        ["hk.title"] = ("Global hotkey", "Глобальная горячая клавиша"), ["hk.press"] = ("Press a key combination, e.g. Ctrl+Alt+1. It works even when the app is in the background.", "Нажмите сочетание клавиш, например Ctrl+Alt+1. Работает, даже когда приложение свернуто."), ["hk.clear"] = ("Remove", "Убрать"),

        // Voice changer
        ["ch.title"] = ("Voice Changer", "Войсченджер"), ["ch.sub"] = ("Real-time effects on your microphone. Only the changed voice goes to the voice chat.", "Эффекты на микрофон в реальном времени. В чат уходит только изменённый голос."),
        ["ch.mic"] = ("Microphone", "Микрофон"), ["ch.mic.sub"] = ("Send my mic to the virtual microphone", "Передавать мой микрофон в виртуальный"),
        ["ch.fx"] = ("Voice effects", "Эффекты голоса"), ["ch.fx.sub"] = ("Off = clean voice passes through", "Выкл = проходит чистый голос"),
        ["ch.hear"] = ("Hear myself", "Слышать себя"), ["ch.hear.sub"] = ("Monitor the result in headphones", "Слушать результат в наушниках"),
        ["ch.input"] = ("Input device", "Устройство ввода"), ["ch.level.in"] = ("Input level", "Уровень входа"), ["ch.level.out"] = ("Processed level", "Уровень после обработки"),
        ["ch.presets"] = ("Presets", "Пресеты"), ["ch.search"] = ("Search presets…", "Поиск пресетов…"), ["presets.count"] = ("{0} of {1}", "{0} из {1}"),
        ["ch.tune"] = ("FINE TUNE", "ТОНКАЯ НАСТРОЙКА"), ["ch.preset.name"] = ("My preset name…", "Название моего пресета…"),
        ["preset.needname"] = ("Enter a preset name", "Введите название пресета"), ["preset.saved"] = ("Preset saved", "Пресет сохранён"),
        ["mic.denied"] = ("Windows blocks the microphone. In the window that just opened turn on “Let desktop apps access your microphone”.", "Windows запрещает доступ к микрофону. В открывшемся окне включите «Разрешить классическим приложениям доступ к микрофону»."),
        ["mic.error"] = ("Could not open the microphone", "Не удалось открыть микрофон"), ["changer.on"] = ("Voice effects ON", "Эффекты голоса ВКЛ"), ["changer.off"] = ("Voice effects OFF", "Эффекты голоса ВЫКЛ"),
        ["cat.all"] = ("All", "Все"), ["cat.clean"] = ("Clean", "Чистые"), ["cat.human"] = ("Human", "Люди"), ["cat.creature"] = ("Creatures", "Существа"), ["cat.robot"] = ("Robots", "Роботы"), ["cat.device"] = ("Devices", "Устройства"), ["cat.space"] = ("Spaces", "Пространства"), ["cat.fun"] = ("Fun", "Весёлые"), ["cat.custom"] = ("My presets", "Мои пресеты"),
        ["fxgroup.tone"] = ("PITCH & TONE", "ТОН И ТЕМБР"), ["fxgroup.mod"] = ("MODULATION", "МОДУЛЯЦИЯ"), ["fxgroup.dirt"] = ("DISTORTION", "ИСКАЖЕНИЯ"), ["fxgroup.space"] = ("SPACE", "ПРОСТРАНСТВО"), ["fxgroup.dyn"] = ("DYNAMICS", "ДИНАМИКА"),
        ["fx.pitch"] = ("Pitch", "Высота тона"), ["fx.bass"] = ("Bass", "Низкие"), ["fx.mid"] = ("Mids", "Средние"), ["fx.treble"] = ("Treble", "Высокие"), ["fx.lowcut"] = ("Low cut", "Срез низов"), ["fx.highcut"] = ("High cut", "Срез верхов"),
        ["fx.robot"] = ("Ring robot", "Ринг-робот"), ["fx.robotfreq"] = ("Robot tone", "Тон робота"), ["fx.mono"] = ("Monotone", "Монотонность"), ["fx.whisper"] = ("Whisper", "Шёпот"),
        ["fx.tremolo"] = ("Tremolo", "Тремоло"), ["fx.tremrate"] = ("Tremolo rate", "Частота тремоло"), ["fx.vibrato"] = ("Vibrato", "Вибрато"), ["fx.vibrate"] = ("Vibrato rate", "Частота вибрато"),
        ["fx.chorus"] = ("Chorus", "Хорус"), ["fx.flanger"] = ("Flanger", "Флэнжер"), ["fx.drive"] = ("Overdrive", "Перегруз"), ["fx.crush"] = ("Bit crush", "Биткраш"),
        ["fx.echo"] = ("Echo", "Эхо"), ["fx.echotime"] = ("Echo time", "Время эха"), ["fx.echofb"] = ("Echo repeats", "Повторы эха"), ["fx.reverb"] = ("Reverb", "Реверберация"), ["fx.room"] = ("Room size", "Размер комнаты"),
        ["fx.gate"] = ("Noise gate", "Шумодав"), ["fx.comp"] = ("Compressor", "Компрессор"), ["fx.gain"] = ("Output gain", "Выходное усиление"),

        // Settings
        ["set.title"] = ("Settings", "Настройки"), ["set.sub"] = ("Themes, window, audio devices and hotkeys.", "Темы, окно, аудиоустройства и горячие клавиши."),
        ["set.themes"] = ("Themes", "Темы"), ["set.themes.count"] = ("{0} themes", "Тем: {0}"),
        ["set.window"] = ("Window", "Окно"), ["set.opacity"] = ("Window opacity", "Прозрачность окна"), ["set.miniopacity"] = ("Pinned overlay opacity", "Прозрачность закреплённых окон"),
        ["set.ontop"] = ("Always on top", "Поверх всех окон"), ["set.ontop.sub"] = ("Keep the main window above other apps", "Главное окно всегда над другими приложениями"),
        ["set.blur"] = ("Glass blur", "Стеклянное размытие"), ["set.blur.sub"] = ("Acrylic backdrop for the Glass themes", "Акриловый фон для «стеклянных» тем"),
        ["set.anim"] = ("Animations", "Анимации"), ["set.anim.sub"] = ("Page transitions and ambient motion", "Переходы страниц и живой фон"),
        ["set.audio"] = ("Audio devices", "Аудиоустройства"), ["set.refresh"] = ("Refresh devices", "Обновить устройства"),
        ["set.voicedev"] = ("Virtual microphone (what others hear)", "Виртуальный микрофон (что слышат другие)"), ["set.voicevol"] = ("Voice chat volume", "Громкость в голосовой чат"),
        ["set.mondev"] = ("My headphones / speakers", "Мои наушники / колонки"), ["set.monvol"] = ("My volume", "Моя громкость"),
        ["set.how"] = ("How others hear you", "Как вас услышат другие"),
        ["set.how.1"] = ("1.  Press “Install driver”: VoxDeck downloads the free signed VB-CABLE driver and sets it up. Windows asks for permission once.", "1.  Нажмите «Установить драйвер»: VoxDeck сам скачает бесплатный подписанный драйвер VB-CABLE и поставит его. Windows один раз спросит разрешение."),
        ["set.cache"] = ("Sound cache", "Кэш звуков"), ["set.cache.tip"] = ("Downloaded online sounds are kept on disk so they play instantly next time. Click to clear.", "Скачанные из сети звуки хранятся на диске, чтобы в следующий раз играть мгновенно. Нажмите, чтобы очистить."),
        ["set.how.2"] = ("2.  VoxDeck finds “CABLE Input” automatically and sends speech, sounds and your changed voice there.", "2.  VoxDeck сам найдёт «CABLE Input» и будет отправлять туда речь, звуки и изменённый голос."),
        ["set.how.3"] = ("3.  With “System microphone” on, every app that uses the default microphone hears VoxDeck — nothing to pick. Otherwise choose “CABLE Output” as the microphone in Discord, Telegram, Steam or the game.", "3.  Пока включено «Микрофон системы», вас слышат все программы с микрофоном по умолчанию — выбирать ничего не нужно. Иначе выберите микрофон «CABLE Output» в Discord, Telegram, Steam или игре."),
        ["set.how.4"] = ("4.  Want to talk too? Turn on Microphone in the Voice Changer tab — your original voice stays hidden.", "4.  Хотите говорить сами? Включите «Микрофон» во вкладке «Войсченджер» — оригинальный голос скрыт."),
        ["set.hotkeys"] = ("Global hotkeys", "Глобальные клавиши"), ["set.hk.stop"] = ("Stop all sounds", "Остановить все звуки"), ["set.hk.changer"] = ("Toggle voice effects", "Вкл/выкл эффекты голоса"),
        ["set.hk.hint"] = ("Format: Ctrl+Alt+S, Shift+F5, Num1… Per-sound hotkeys: right-click a pad.", "Формат: Ctrl+Alt+S, Shift+F5, Num1… Клавиши для звуков: правый клик по пэду."),
        ["set.about"] = ("Text to speech · Soundpad · Voice changer", "Текст в речь · Саундпад · Войсченджер"), ["set.data"] = ("Open data folder", "Папка с данными"),

        // overlays
        ["mini.tts"] = ("Quick TTS", "Быстрый TTS"), ["mini.pad"] = ("Soundpad", "Саундпад"), ["mini.opacity"] = ("Opacity", "Прозрачность"),
        ["mini.hint"] = ("Type and press Enter…", "Введите текст и нажмите Enter…"), ["mini.pad.hint"] = ("Shows your favorites. Does not steal focus from the game.", "Показывает избранное. Не забирает фокус у игры."),
    };
}

/// <summary>XAML: Text="{l:T key}" — live-updating localized string.</summary>
public class T : MarkupExtension
{
    public string Key { get; set; }
    public T() { }
    public T(string key) { Key = key; }
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
}
