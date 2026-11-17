using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

/// <summary>
/// Приводит параметры решения модели к идентификаторам мира: «Кузня» → forge,
/// «Богдан» → bohdan, «Скриня» → chest. Модель видит в промпте и id, и название,
/// но отвечает то так, то так — игра обязана понимать оба варианта.
/// </summary>
public static class DecisionNormalizer
{
    public static LlmDecision Normalize(World world, LlmDecision decision)
    {
        return decision with
        {
            Zone = ResolveZone(world, decision.Zone) ?? decision.Zone,
            Target = ResolveCharacter(world, decision.Target) ?? decision.Target,
            Item = ResolveItem(world, decision.Item) ?? decision.Item
        };
    }

    public static string? ResolveZone(World world, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Clean(value);
        foreach (var zone in world.Map.Zones)
        {
            if (Matches(text, zone.Id) || Matches(text, zone.NameUk))
            {
                return zone.Id;
            }
        }

        return null;
    }

    public static string? ResolveCharacter(World world, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Clean(value);
        if (Matches(text, Player.PlayerId) || Matches(text, "гравець") || Matches(text, "гравец") ||
            Matches(text, "игрок"))
        {
            return Player.PlayerId;
        }

        foreach (var npc in world.Npcs)
        {
            if (Matches(text, npc.Id) || Matches(text, npc.Name))
            {
                return npc.Id;
            }
        }

        return null;
    }

    public static string? ResolveItem(World world, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = Clean(value);
        foreach (var item in world.Items.Values)
        {
            if (Matches(text, item.Id) || Matches(text, item.NameUk))
            {
                return item.Id;
            }
        }

        return null;
    }

    private static string Clean(string value)
    {
        return value.Trim().Trim('"', '\'', '«', '»', '.', ',', '!', '?').ToLowerInvariant();
    }

    /// <summary>Совпадение по id/названию с учётом падежей: «кузні» тоже сработает.</summary>
    private static bool Matches(string text, string candidate)
    {
        var other = candidate.ToLowerInvariant();
        if (string.Equals(text, other, StringComparison.Ordinal))
        {
            return true;
        }

        var minLength = Math.Min(text.Length, other.Length);
        if (minLength < 4)
        {
            return false;
        }

        // общая основа в 4+ символа: «кузн» у «кузня» и «кузні»
        var prefix = 0;
        while (prefix < minLength && text[prefix] == other[prefix])
        {
            prefix++;
        }
        if (prefix >= 4 && prefix >= minLength - 1)
        {
            return true;
        }

        return text.Contains(other, StringComparison.Ordinal) || other.Contains(text, StringComparison.Ordinal);
    }
}
