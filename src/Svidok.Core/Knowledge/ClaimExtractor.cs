using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Knowledge;

/// <summary>Заявления игрока (раздел 8.8): текст игрока не создаёт знание с c больше доверия к нему.</summary>
public static class ClaimExtractor
{
    private static readonly string[] QuestKeywords =
    [
        "скриня", "скриню", "скрині", "приніс", "принес", "виконав", "віддав", "отдав"
    ];

    public static bool HasClaimKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        var lower = text.ToLowerInvariant();
        foreach (var keyword in QuestKeywords)
        {
            if (lower.Contains(keyword, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Создаёт запись PlayerClaim, если в тексте есть ключевые слова задания.</summary>
    public static KnowledgeEntry? Process(World world, Npc npc, string text)
    {
        if (!HasClaimKeywords(text))
        {
            return null;
        }

        var trust = world.Trust.Get(npc.Id, Player.PlayerId);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["actor"] = Player.PlayerId,
            ["target"] = npc.Id,
            ["text"] = text
        };

        var quest = world.Quests.QuestOfGiver(npc.Id);
        if (quest is not null)
        {
            fields["questId"] = quest.Id;
        }

        var importance = world.Schema.Importance("give_item");
        return npc.Knowledge.AddClaim("player_claim", fields, trust, world.Tick, Player.PlayerId, importance);
    }
}
