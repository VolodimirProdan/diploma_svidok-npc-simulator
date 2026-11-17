using Svidok.Core.Config;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Knowledge;

/// <summary>
/// Искажения при пересказе (раздел 8.7). Их делает игра, не модель.
/// На каждый пересказ тратится ровно три случайных числа, чтобы поток RngRumor
/// не зависел от достоверности и разные trustMultiplier сравнивались на одних и тех же разговорах.
/// </summary>
public sealed class DistortionEngine
{
    private readonly SimulationConfig _config;
    private readonly EventSchema _schema;
    private readonly Dictionary<string, string[]> _itemGroups = new(StringComparer.Ordinal)
    {
        ["valuable"] = ["silver_cup", "sword"],
        ["goods"] = ["grain_sack", "bread"]
    };

    public DistortionEngine(SimulationConfig config, EventSchema schema)
    {
        _config = config;
        _schema = schema;
    }

    public double Probability(double sourceC) =>
        _config.PDistortBase + (_config.PDistortByDoubt * (1.0 - Credibility.Clamp(sourceC)));

    /// <summary>Готовит копию записи так, как её перескажет рассказчик.</summary>
    public KnowledgeEntry BuildRetelling(KnowledgeEntry original, Npc narrator, World world, Random random)
    {
        var copy = original.Clone();
        copy.SharedWith = new HashSet<string>(original.SharedWith, StringComparer.Ordinal);

        var roll = random.NextDouble();
        var def = _schema.Get(original.Type);
        var candidates = def.Distortable.Count > 0
            ? def.Distortable.Where(f => copy.Fields.ContainsKey(f)).ToList()
            : new List<string>();
        var fieldRoll = random.NextDouble();
        var valueRoll = random.NextDouble();

        if (roll >= Probability(original.C) || candidates.Count == 0)
        {
            return copy;
        }

        var field = candidates[Math.Min(candidates.Count - 1, (int)(fieldRoll * candidates.Count))];
        if (!copy.Fields.TryGetValue(field, out var value))
        {
            return copy;
        }

        switch (field)
        {
            case "actor":
                copy.Fields["actor"] = PickSeenActor(narrator, world, valueRoll);
                break;
            case "item":
                copy.Fields["item"] = PickOtherItem(value, world, valueRoll);
                break;
            case "zone":
                copy.Fields["zone"] = PickNeighbourZone(value, valueRoll);
                break;
            case "price":
            case "coins":
                copy.Fields[field] = ScaleNumber(value, valueRoll);
                break;
        }

        copy.IsDistorted = true;
        return copy;
    }

    private static string PickSeenActor(Npc narrator, World world, double roll)
    {
        var threshold = world.Tick - 360;
        var seen = narrator.SeenNpcAtTick
            .Where(pair => pair.Value >= threshold)
            .Select(pair => pair.Key)
            .Where(id => world.Npc(id) is not null)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (seen.Count == 0)
        {
            return "хтось";
        }
        return seen[Math.Min(seen.Count - 1, (int)(roll * seen.Count))];
    }

    private string PickOtherItem(string itemId, World world, double roll)
    {
        var category = ItemCategoriesFor(itemId, world);
        if (!_itemGroups.TryGetValue(category, out var group) || group.Length < 2)
        {
            return "щось";
        }
        var alternatives = group.Where(i => !string.Equals(i, itemId, StringComparison.Ordinal)).ToList();
        if (alternatives.Count == 0)
        {
            return "щось";
        }
        return alternatives[Math.Min(alternatives.Count - 1, (int)(roll * alternatives.Count))];
    }

    private static string ItemCategoriesFor(string itemId, World world)
    {
        if (world.Items.TryGetValue(itemId, out var item))
        {
            return item.Category;
        }
        return itemId is "silver_cup" or "sword" ? ItemCategories.Valuable : ItemCategories.Goods;
    }

    private static string PickNeighbourZone(string zoneId, double roll)
    {
        var zone = DefaultZones.ById(zoneId);
        if (zone is null || zone.Neighbours.Count == 0)
        {
            var all = DefaultZones.All;
            return all[Math.Min(all.Count - 1, (int)(roll * all.Count))].Id;
        }
        return zone.Neighbours[Math.Min(zone.Neighbours.Count - 1, (int)(roll * zone.Neighbours.Count))];
    }

    private static string ScaleNumber(string value, double roll)
    {
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            return value;
        }
        var scaled = roll < 0.5 ? number * 0.5 : number * 2.0;
        if (Math.Abs(number - Math.Round(number)) < 0.0001)
        {
            return ((int)Math.Round(scaled)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return scaled.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
