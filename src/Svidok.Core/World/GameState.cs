namespace Svidok.Core.WorldModel;

/// <summary>Игрок предложил обмен: свой предмет на предмет NPC.</summary>
public sealed record TradeOffer(string PlayerItem, string? NpcItem, int Tick);

/// <summary>NPC потребовал плату: монеты и, возможно, предмет, который он отдаст взамен.</summary>
public sealed record PaymentDemand(int Coins, string? Item, int Tick);

/// <summary>NPC подошёл к игроку сам и хочет говорить: причина и первая фраза.</summary>
public sealed record IncomingTalk(string NpcId, string Reason, string Opening, int Tick);

/// <summary>Игрок заплатил NPC.</summary>
public sealed record PlayerPayment(int Tick, string NpcId, int Coins);

/// <summary>
/// Состояние игровых механик сценариев S6–S8. В экспериментах S1–S5 все функции выключены,
/// и мир ведёт себя как раньше.
/// </summary>
public sealed class GameState
{
    /// <summary>Поступки игрока, реакции NPC, лояльность, подкуп, подстава, арест и изгнание игрока.</summary>
    public bool Social { get; set; }

    /// <summary>Здоровье и драки.</summary>
    public bool Combat { get; set; }

    /// <summary>Голод, еда, подработка, свой дом, комендантский час.</summary>
    public bool Life { get; set; }

    public bool AnyEnabled => Social || Combat || Life;

    public Dictionary<string, TradeOffer> Trades { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, PaymentDemand> Demands { get; } = new(StringComparer.Ordinal);

    public List<PlayerPayment> Payments { get; } = new();

    /// <summary>Слух не передан, потому что NPC пообещал молчать.</summary>
    public int SecretBlocked { get; set; }

    /// <summary>Слух передан, хотя NPC обещал молчать (проболтался).</summary>
    public int SecretLeaked { get; set; }

    /// <summary>NPC сознательно раскрыл секрет действием (warn, report_to_guard, shout_alarm).</summary>
    public int PromiseBreaks { get; set; }

    /// <summary>Подброшенный предмет найден.</summary>
    public int ItemsFound { get; set; }

    /// <summary>Кого арестовали (по порядку).</summary>
    public List<string> Arrests { get; } = new();

    /// <summary>S7: кого подставили и кто настоящий вор (для метрики ложных арестов).</summary>
    public string? FramedNpcId { get; set; }

    public string? TrueCulpritId { get; set; }

    /// <summary>Кровать каждого жителя в его доме: туда он идёт спать, а не ложится где застала ночь.</summary>
    public Dictionary<string, (int X, int Y)> Beds { get; } = new(StringComparer.Ordinal);

    /// <summary>Кровать игрока в его доме (если дом есть).</summary>
    public (int X, int Y)? PlayerBed { get; set; }

    /// <summary>Пары друзей и родни: через них о игроке передают доброе или худое слово.</summary>
    public List<(string A, string B)> Friends { get; } = new();

    /// <summary>Сюжеты сценария (data/stories).</summary>
    public List<Scenarios.StoryState> Stories { get; } = new();

    /// <summary>Что сейчас происходит в селе по сюжету: строка для промпта жителей.</summary>
    public string StoryContext { get; set; } = string.Empty;

    /// <summary>Сообщения сюжета для игрока.</summary>
    public Queue<string> StoryNotices { get; } = new();

    /// <summary>Кто сейчас агитирует за себя.</summary>
    public HashSet<string> Campaigners { get; } = new(StringComparer.Ordinal);

    /// <summary>Кто сейчас староста (может смениться по сюжету; player — игрок).</summary>
    public string? HeadmanId { get; set; }

    /// <summary>Какие знания уже повлияли на доверие (житель|запись).</summary>
    public HashSet<string> ReputationSeen { get; } = new(StringComparer.Ordinal);

    /// <summary>Игрок управляет персонажем (а не наблюдает): только тогда жители сами к нему подходят.</summary>
    public bool PlayerActive { get; set; }

    /// <summary>Кто дошёл до игрока и ждёт разговора.</summary>
    public Queue<IncomingTalk> IncomingTalks { get; } = new();

    /// <summary>Когда каждый житель последний раз сам шёл к игроку.</summary>
    public Dictionary<string, int> LastApproach { get; } = new(StringComparer.Ordinal);

    /// <summary>По какому поводу житель уже приходил (чтобы не приходил с тем же снова).</summary>
    public HashSet<string> ApproachReasonsUsed { get; } = new(StringComparer.Ordinal);

    public int LastAnyApproachTick { get; set; } = -10000;

    /// <summary>С кем игрок уже говорил: эти не подходят «знакомиться».</summary>
    public HashSet<string> Acquainted { get; } = new(StringComparer.Ordinal);

    /// <summary>Когда последний раз кто-то подходил просто познакомиться (не чаще раза в три часа).</summary>
    public int LastMeetTick { get; set; } = -10000;

    /// <summary>Кого и когда обыскивали по какому событию: повторно не обыскивают, пока не пройдёт время.</summary>
    public Dictionary<(int EventRef, string TargetId), (int Tick, bool Found)> Searches { get; } = new();

    /// <summary>Номер ночи, за которую игрока уже оштрафовали за комендантский час.</summary>
    public int CurfewFinedNight { get; set; } = -1;

    /// <summary>Сколько монет игрок заплатил этому NPC за последние ticks тиков.</summary>
    public int PaidRecently(string npcId, int tick, int ticks)
    {
        var total = 0;
        foreach (var payment in Payments)
        {
            if (string.Equals(payment.NpcId, npcId, StringComparison.Ordinal) && tick - payment.Tick <= ticks)
            {
                total += payment.Coins;
            }
        }
        return total;
    }
}

/// <summary>Числа игровых механик в одном месте.</summary>
public static class GameRules
{
    public const int JailTicks = 120;

    /// <summary>С чего начинается доверие к новичку в игровых сценариях («насторожено», но говорить можно).</summary>
    public const double NewcomerTrust = 0.4;
    public const int BanishTicks = 240;
    public const int RefuseTalkTicks = 30;
    public const int KnockoutTicks = 60;
    public const int MinHit = 2;
    public const int MaxHit = 4;
    public const int DemandTtlTicks = 60;
    public const int TradeTtlTicks = 30;
    public const int MaxDemandCoins = 50;

    /// <summary>Подкуп: сколько тиков после оплаты NPC считает её «только что».</summary>
    public const int PaymentWindowTicks = 30;

    /// <summary>Обещание молчать слабеет за игровой час.</summary>
    public const double SecretDecayPerHour = 0.02;

    /// <summary>Ниже этой силы обещание уже не держит валидатор V4.</summary>
    public const double SecretHoldThreshold = 0.3;

    public const double HungerPerHour = 4.0;
    public const double StarvingHunger = 80.0;
    public const int CurfewFromMinute = 22 * 60;
    public const int CurfewToMinute = 5 * 60;
    public const int CurfewFine = 2;
    public const int WorkTicks = 30;
    public const int WorkPay = 2;
    public const double WorkHunger = 6.0;
}
