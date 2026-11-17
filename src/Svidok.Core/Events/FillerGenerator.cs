using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Events;

/// <summary>
/// Фоновые события: каждый игровой час для каждого бодрствующего NPC
/// генерируется Poisson(λ) событий, тип — по весам с учётом текущей Activity (раздел 6.3).
/// Использует только RngFiller.
/// </summary>
public sealed class FillerGenerator
{
    private readonly EventSchema _schema;

    public FillerGenerator(EventSchema schema)
    {
        _schema = schema;
    }

    public List<WorldEvent> GenerateHour(World world)
    {
        var created = new List<WorldEvent>();
        var lambda = world.Config.FillerPerNpcPerHour;
        if (lambda <= 0)
        {
            return created;
        }

        foreach (var npc in world.ActiveNpcs())
        {
            if (npc.IsAsleep || npc.SpawnTick > world.Tick)
            {
                continue;
            }

            var count = world.Rng.Poisson(lambda, world.Rng.Filler);
            for (var i = 0; i < count; i++)
            {
                var eventType = PickType(npc, world.Rng.Filler);
                var target = PickTarget(world, npc);
                if (target is null && eventType is "greet" or "talk" or "buy")
                {
                    eventType = "work";
                }

                var extra = new Dictionary<string, string>(StringComparer.Ordinal);
                var zoneId = world.ZoneOfTile(npc.X, npc.Y);
                var targetId = target?.Id;
                var itemId = (string?)null;

                switch (eventType)
                {
                    case "buy":
                        itemId = world.Rng.Chance(0.5, world.Rng.Filler) ? "bread" : "grain_sack";
                        extra["price"] = world.Rng.NextInt(2, 12, world.Rng.Filler).ToString();
                        break;
                    case "greet":
                    case "talk":
                        extra["text"] = eventType == "greet" ? "Добрий день." : "Як справи?";
                        break;
                    case "eat":
                        itemId = "bread";
                        break;
                }

                var def = _schema.Get(eventType);
                var worldEvent = world.Log.Add(
                    world.Tick,
                    eventType,
                    npc.Id,
                    targetId,
                    itemId,
                    zoneId,
                    npc.X,
                    npc.Y,
                    def.Loudness,
                    def.Importance,
                    true,
                    extra);
                created.Add(worldEvent);
            }
        }

        return created;
    }

    private static string PickType(Npc npc, Random random)
    {
        return npc.Activity switch
        {
            Activity.Trade => Weighted(random, ("buy", 0.5), ("greet", 0.3), ("talk", 0.2)),
            Activity.Work => Weighted(random, ("work", 0.7), ("greet", 0.3)),
            Activity.Rest => Weighted(random, ("eat", 0.4), ("talk", 0.4), ("greet", 0.2)),
            Activity.Eat => Weighted(random, ("eat", 0.6), ("talk", 0.2), ("greet", 0.2)),
            Activity.Patrol => Weighted(random, ("work", 0.5), ("greet", 0.3), ("talk", 0.2)),
            Activity.Walk => Weighted(random, ("greet", 0.4), ("talk", 0.3), ("work", 0.3)),
            Activity.Talk => Weighted(random, ("talk", 0.5), ("greet", 0.3), ("work", 0.2)),
            Activity.React => Weighted(random, ("work", 0.6), ("greet", 0.4)),
            _ => "work"
        };
    }

    private static string Weighted(Random random, params (string Type, double Weight)[] options)
    {
        var total = 0.0;
        foreach (var option in options)
        {
            total += option.Weight;
        }
        var roll = random.NextDouble() * total;
        var acc = 0.0;
        foreach (var option in options)
        {
            acc += option.Weight;
            if (roll <= acc)
            {
                return option.Type;
            }
        }
        return options[^1].Type;
    }

    private static Npc? PickTarget(World world, Npc npc)
    {
        var nearby = new List<Npc>();
        foreach (var other in world.ActiveNpcs())
        {
            if (ReferenceEquals(other, npc) || other.IsAsleep)
            {
                continue;
            }
            if (Pathfinding.Euclidean(other.Position, npc.Position) <= 3.0)
            {
                nearby.Add(other);
            }
        }
        if (nearby.Count == 0)
        {
            return null;
        }
        return nearby[world.Rng.NextInt(0, nearby.Count, world.Rng.Filler)];
    }
}
