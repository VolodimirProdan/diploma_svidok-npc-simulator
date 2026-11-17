namespace Svidok.Game;

/// <summary>Все тексты интерфейса на украинском (раздел 18 ТЗ).</summary>
public static class UiText
{
    public const string AppTitle = "Свідок";

    public const string AppSubtitle = "Симулятор поведінки NPC з обмеженою обізнаністю";

    public const string ChooseScenario = "Оберіть сценарій";

    public const string Mode = "Режим";

    public const string ObserverMode = "Спостерігач";

    public const string PlayerMode = "Гравець";

    public const string Variant = "Варіант";

    public const string Seed = "Seed";

    public const string Start = "Почати";

    public const string Back = "До меню";

    public const string Continue = "Продовжити";

    public const string Restart = "Перезапустити";

    public const string Close = "Закрити";

    public const string Create = "Створити";

    public const string Cancel = "Скасувати";

    public const string Pause = "Пауза";

    public const string TabCharacter = "Персонаж";

    public const string TabJournal = "Журнал подій";

    public const string TabRumors = "Чутки";

    public const string TabLlm = "Нейромережа";

    public const string TabStats = "Статистика";

    public const string AskLlm = "Запитати нейромережу";

    public const string ExportCsv = "Експорт CSV";

    public const string TakeQuest = "Взяти завдання";

    public const string GiveItem = "Віддати предмет";

    public const string ShowTech = "Показати технічні дані";

    public const string Copy = "Копіювати";

    public const string NoApiKey = "Ключ API не знайдено";

    public const string ApiKeyFound = "Ключ API знайдено";

    public const string ModelMode = "Режим моделі";

    public const string Recommended = "Рекомендовані";

    public const string Loading = "Зачекайте…";

    public const string SpeedPause = "Пауза";

    public static string SpeedLabel(int index)
    {
        return index switch
        {
            0 => "Пауза",
            1 => "×1",
            2 => "×4",
            3 => "×16",
            4 => "×64",
            _ => "×1"
        };
    }

    public static string GameTime(string text) => text;

    public const string Inventory = "Інвентар";

    public const string Health = "Стан";

    public const string DialogHint = "Enter — надіслати, Esc — закрити";

    public const string SpeakHint = "E — говорити з NPC";

    public const string Empty = "(порожньо)";

    public const string Yes = "так";

    public const string No = "ні";

    public const string Unknown = "невідомо";

    public const string Truth = "правда";

    public const string WhatNpcKnows = "що знає";

    public const string CreateEvent = "Створити подію";

    public const string EventType = "Тип події";

    public const string Actor = "Хто";

    public const string Target = "Кого / що";

    public const string Item = "Предмет";

    public const string Zone = "Зона";

    public const string Tile = "Тайл";

    public const string MoveCamera = "ПКМ або СКМ + рух — камера, колесо — масштаб, Пробел — пауза";

    public const string Resume = "Продовжити";

    public const string Values = "Значення";

    public const string Total = "Всього";

    public const string Background = "Фонових";

    public const string LogSize = "Розмір журналу";

    public const string TokensFull = "Токенів у повному журналі";

    public const string Requests = "Запитів";

    public const string Cost = "Вартість";

    public const string Queue = "У черзі";

    public const string ActiveRequests = "Активних";

    public const string AvgLatency = "Середня затримка";

    public const string Rejections = "Відмови валідатора";

    public const string ParseErrors = "Помилки розбору";

    public const string Coverage = "Покриття";

    public const string MeanC = "Середня c";

    public const string Distorted = "Спотворених";

    public const string T50 = "T50";

    public const string T90 = "T90";

    public const string NotReached = "не досягнуто";

    public const string NoData = "немає даних";

    public const string PlayerDialogTitle = "Розмова";

    public const string PlayerShoutTitle = "Крик";

    public const string ShoutText = "Що крикнути?";

    public const string ShoutButton = "Крикнути";

    public const string PickedUp = "Підібрано";

    public const string Stolen = "Украдено";

    public const string Given = "Передано";

    public const string NothingHere = "Тут нічого немає";

    public const string NeedTarget = "Поруч нікого немає";

    public const string Paused = "Пауза";

    public const string Finished = "Симуляцію завершено";

    public const string GoalsTitle = "Що можна перевірити";

    public const string GoalsButton = "Підказки";

    public const string ScenarioOverTitle = "Сценарій завершено";

    public const string AiTitle = "Штучний інтелект (DeepSeek)";

    public const string AiOn = "ШІ увімкнено: у варіантах V1–V4 NPC відповідають через нейромережу.";

    public const string AiOff = "ШІ вимкнено: доступний лише V0, де NPC говорять заготовленими фразами.";

    public const string AiKeyHint = "вставте ключ sk-…";

    public const string AiEnable = "Увімкнути ШІ";

    public const string AiDisable = "Вимкнути ШІ";

    public const string AiKeyNote = "Ключ діє, поки відкрита програма, і нікуди не записується.";

    public const string AiNeedKey = "Для цього варіанту потрібен ключ: введіть його вище або оберіть V0.";

    public const string ForExperiments = "Для експериментів";

    public const string V0Warning =
        "Зараз варіант V0: NPC відповідають заготовленими фразами без ШІ. Щоб поговорити з нейромережею, " +
        "оберіть у меню V1–V4 і введіть ключ.";

    public static string VariantLabel(string id)
    {
        return id.Trim().ToUpperInvariant() switch
        {
            "V0" => "V0 · правила, без ШІ",
            "V1" => "V1 · ШІ бачить увесь журнал",
            "V2" => "V2 · ШІ знає лише своє",
            "V3" => "V3 · своє + довіра до джерел",
            "V4" => "V4 · повний метод з перевіркою",
            _ => id
        };
    }

    public static string VariantHint(string id)
    {
        return id.Trim().ToUpperInvariant() switch
        {
            "V0" => "Без нейромережі: прості правила і кілька готових фраз. Працює без ключа.",
            "V1" => "Нейромережа бачить усе, що сталося в селі, навіть те, чого персонаж не бачив. Так роблять «у лоб».",
            "V2" => "Нейромережа знає лише те, що персонаж бачив або почув, без оцінки, наскільки цьому вірити.",
            "V3" => "Як V2, плюс персонаж знає, від кого почув і наскільки цьому вірить.",
            "V4" => "Метод з диплому: власні знання, довіра до джерел і перевірка дій перед виконанням.",
            _ => string.Empty
        };
    }

    public static string ModelModeLabel(string mode)
    {
        return mode switch
        {
            "Live" => "Live — щоразу питати модель",
            "LiveWithCache" => "LiveWithCache — брати з кешу, якщо вже питали",
            "ReplayOnly" => "ReplayOnly — лише кеш, без мережі",
            "Mock" => "Mock — заглушка без мережі",
            _ => mode
        };
    }

    public static string ActionNameUk(string actionId)
    {
        return actionId switch
        {
            "give_reward" => "видає нагороду",
            "refuse" => "відмовляє",
            "report_to_guard" => "кличе варту",
            "arrest" => "затримує",
            "shout_alarm" => "здіймає тривогу",
            "warn" => "попереджає інших",
            "investigate" => "іде подивитися",
            "go_to" => "кудись іде",
            "hide_valuables" => "ховає цінне",
            "flee" => "тікає",
            "help_fire" => "гасить пожежу",
            "close_shop" => "зачиняє крамницю",
            "give_item" => "дає предмет",
            "end_talk" => "обриває розмову",
            "demand_payment" => "вимагає плату",
            "keep_secret" => "обіцяє мовчати",
            "treat" => "пригощає",
            "give_quest" => "дає завдання",
            "accept_trade" => "погоджується на обмін",
            "banish" => "виганяє з селища",
            "fine" => "штрафує",
            "attack" => "б'ється",
            "search" => "іде обшукувати",
            "approach_player" => "іде до вас",
            _ => actionId
        };
    }

    /// <summary>Коротко: что происходит в сценарии и зачем он нужен.</summary>
    public static string ScenarioPurpose(string id, string fallback)
    {
        return id.Trim().ToLowerInvariant() switch
        {
            "s1" => "Зранку на ринку крадуть срібний кубок. Сценарій показує, як чутка розходиться селом і слабшає з кожним переказом.",
            "s3" => "Корчмар Богдан обіцяв нагороду за скриню зі складу. Перевіряємо, чи можна вмовити його заплатити без скрині.",
            "s4" => "Тиждень життя села: журнал подій росте, а знання одного персонажа лишаються маленькими.",
            "s5" => "У кузні пожежа. Очевидці гасять одразу, інші дізнаються з крику або від сусідів.",
            "s6" => "Пісочниця: усі механіки ввімкнені. Ви — гравець у селі, де кожен знає лише те, що бачив або чув сам. Можна підкуповувати, мінятися, підкидати речі й битися.",
            "s7_open" => "Крадіжка на ринку, свідка ніхто не просить мовчати. Контрольний запуск: як швидко розходиться чутка.",
            "s7_secret" => "Та сама крадіжка, але свідку заплатили за мовчання. Перевіряємо, чи тримає персонаж обіцянку і чи зупиняється чутка.",
            "s7_frame" => "Вкрадене підкинули в чужу хату. Перевіряємо, чи арештують невинного і чи зупинить це перевірка рішень.",
            "s8" => "Живе село: велика мапа, родини, свій дім, голод, підробіток, комендантська година, бійки без смертей.",
            _ when id.StartsWith("s2", StringComparison.OrdinalIgnoreCase) =>
                "Вночі на складі щось сталося. Потім селян питають про це: хто справді знає, а хто «знає» лише тому, що ШІ бачить увесь журнал.",
            _ => fallback
        };
    }

    /// <summary>На что смотреть в режиме наблюдателя.</summary>
    public static string[] ObserverTips(string id)
    {
        var common = new[]
        {
            "Клікніть на персонажа: праворуч видно, що він знає, від кого почув і наскільки вірить (c від 0 до 1).",
            "R — теплова карта: червоне кільце — знає впевнено, жовте — сумнівається, сіре — чув краєм вуха.",
            "1–5 — швидкість, Пробіл — пауза."
        };
        var specific = id.Trim().ToLowerInvariant() switch
        {
            "s1" => new[]
            {
                "Вкладка «Чутки» показує, як змінювався переказ від людини до людини і де з'явились спотворення.",
                "Вкладка «Статистика» — скільки людей уже знає (покриття) і як падає достовірність."
            },
            "s3" => new[]
            {
                "Цей сценарій цікавіший у режимі гравця: спробуйте самі обдурити Богдана з V1 і з V4.",
                "Сценарій триває одну годину, 17:00–18:00."
            },
            "s4" => new[] { "Вкладка «Статистика»: розмір журналу і скільки токенів коштував би повний журнал." },
            "s5" => new[] { "Порівняйте V3 і V4: хто і як швидко реагує. Вкладка «Нейромережа» показує кожен запит." },
            "s6" => new[] { "Найцікавіше тут — режим гравця з V4." },
            _ when id.StartsWith("s2", StringComparison.OrdinalIgnoreCase) => new[]
            {
                "Вкладка «Нейромережа»: відповіді персонажів. У V1 вони називають злодія, якого не бачили, у V4 — ні."
            },
            _ => Array.Empty<string>()
        };
        return specific.Concat(common).ToArray();
    }
}
