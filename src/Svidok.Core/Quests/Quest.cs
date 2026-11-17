namespace Svidok.Core.Quests;

public enum QuestState
{
    NotTaken,
    Taken,
    Completed,
    Rewarded
}

/// <summary>Вид задания: принести предмет, найти потерянное, отнести посылку, помочь с работой.</summary>
public enum QuestKind
{
    Bring,
    Find,
    Deliver,
    Help
}

public sealed class Quest
{
    public Quest(string id, string giverId, string titleUk, string requiredItem, int rewardCoins, QuestState state)
    {
        Id = id;
        GiverId = giverId;
        TitleUk = titleUk;
        RequiredItem = requiredItem;
        RewardCoins = rewardCoins;
        State = state;
    }

    public string Id { get; }

    public string GiverId { get; }

    public string TitleUk { get; }

    public string RequiredItem { get; }

    public int RewardCoins { get; }

    public QuestState State { get; set; }

    public bool IsCompletedOrRewarded => State is QuestState.Completed or QuestState.Rewarded;

    public QuestKind Kind { get; set; } = QuestKind.Bring;

    /// <summary>Кому отнести (для Deliver).</summary>
    public string? RecipientId { get; set; }

    /// <summary>Где искать или работать (для Find и Help).</summary>
    public string? ZoneId { get; set; }

    /// <summary>Доручення из села: плата выдаётся сразу по выполнении, без отдельной просьбы.</summary>
    public bool AutoReward { get; set; }

    /// <summary>Подсказка игроку, что делать.</summary>
    public string HintUk { get; set; } = string.Empty;

    public int CreatedTick { get; set; }
}

/// <summary>Задания (раздел 9.10). Выполнение определяет только игра.</summary>
public sealed class QuestSystem
{
    private readonly Dictionary<string, Quest> _quests = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, Quest> All => _quests;

    public void Add(Quest quest) => _quests[quest.Id] = quest;

    public bool Remove(string id) => _quests.Remove(id);

    public Quest? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return _quests.TryGetValue(id, out var quest) ? quest : null;
    }

    public bool TryTake(string? id)
    {
        var quest = Get(id);
        if (quest is null || quest.State != QuestState.NotTaken)
        {
            return false;
        }
        quest.State = QuestState.Taken;
        return true;
    }

    public bool TryComplete(string? id)
    {
        var quest = Get(id);
        if (quest is null || quest.State != QuestState.Taken)
        {
            return false;
        }
        quest.State = QuestState.Completed;
        return true;
    }

    public bool TryReward(string? id)
    {
        var quest = Get(id);
        if (quest is null || quest.State != QuestState.Completed)
        {
            return false;
        }
        quest.State = QuestState.Rewarded;
        return true;
    }

    /// <summary>Задание этого NPC: сначала начатое, потом не взятое, потом любое.</summary>
    public Quest? QuestOfGiver(string npcId)
    {
        Quest? open = null;
        Quest? any = null;
        foreach (var quest in _quests.Values)
        {
            if (!string.Equals(quest.GiverId, npcId, StringComparison.Ordinal))
            {
                continue;
            }
            if (quest.State is QuestState.Taken or QuestState.Completed)
            {
                return quest;
            }
            if (quest.State == QuestState.NotTaken)
            {
                open ??= quest;
            }
            any ??= quest;
        }
        return open ?? any;
    }
}
