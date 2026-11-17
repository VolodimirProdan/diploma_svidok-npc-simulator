using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Perception;

/// <summary>Фильтр восприятия: какие события журнала попадают в память каждого NPC (раздел 7).</summary>
public sealed class PerceptionSystem
{
    private readonly EventSchema _schema;

    public PerceptionSystem(EventSchema schema)
    {
        _schema = schema;
    }

    public double VisionRadius(World world, WorldEvent worldEvent)
    {
        var def = _schema.Get(worldEvent.Type);
        var radius = world.Config.VisionRadius * def.VisionMultiplier;
        if (world.Clock.IsNight(world.Config) && !def.IgnoresNight)
        {
            radius *= world.Config.NightVisionFactor;
        }
        return radius;
    }

    public double HearingRadius(World world, WorldEvent worldEvent, int blockers)
    {
        return world.Config.HearingRadius * worldEvent.Loudness * Math.Pow(0.5, blockers);
    }

    public void Process(World world, IReadOnlyList<WorldEvent> newEvents)
    {
        if (newEvents.Count == 0)
        {
            return;
        }

        foreach (var worldEvent in newEvents)
        {
            var def = _schema.Get(worldEvent.Type);
            var eventPosition = (worldEvent.X, worldEvent.Y);
            var visionRadius = VisionRadius(world, worldEvent);

            foreach (var npc in world.Npcs)
            {
                if (npc.SpawnTick > world.Tick)
                {
                    continue;
                }

                if (string.Equals(npc.Id, worldEvent.ActorId, StringComparison.Ordinal) ||
                    string.Equals(npc.Id, worldEvent.TargetId, StringComparison.Ordinal))
                {
                    npc.Knowledge.AddFirstHand(worldEvent, Source.Participant, 1.0, false, world.Tick, npc.Id);
                    continue;
                }

                var distance = Pathfinding.Euclidean(npc.Position, eventPosition);
                var asleep = npc.IsAsleep;

                if (def.Visual && !asleep && distance <= visionRadius &&
                    LineOfSight.IsClear(world.Map, npc.Position, eventPosition))
                {
                    UpdateTrustFromFirstHand(world, npc, worldEvent);
                    npc.Knowledge.AddFirstHand(worldEvent, Source.Witnessed, 1.0, false, world.Tick, npc.Id);
                    continue;
                }

                if (!def.Audible)
                {
                    continue;
                }
                if (asleep && worldEvent.Loudness < 1.5)
                {
                    continue;
                }

                var blockers = LineOfSight.CountBlockers(world.Map, npc.Position, eventPosition);
                var hearingRadius = HearingRadius(world, worldEvent, blockers);
                if (distance > hearingRadius)
                {
                    continue;
                }

                var maskActor = distance > 3;
                UpdateTrustFromFirstHand(world, npc, worldEvent);
                npc.Knowledge.AddFirstHand(worldEvent, Source.Heard, world.Config.HeardCredibility, maskActor, world.Tick, npc.Id);
            }
        }
    }

    /// <summary>
    /// Если NPC уже знал о событии из пересказа, а теперь увидел его сам:
    /// совпали поля — доверие рассказчику растёт, разошлись — падает (8.5).
    /// </summary>
    private static void UpdateTrustFromFirstHand(World world, Npc npc, WorldEvent worldEvent)
    {
        var existing = npc.Knowledge.ByEventId(worldEvent.Id);
        if (existing is null || Sources.IsFirstHand(existing.Source))
        {
            return;
        }

        var storyTeller = existing.Chain.Count >= 2
            ? existing.Chain[^2]
            : existing.OriginId;
        if (string.IsNullOrWhiteSpace(storyTeller) || string.Equals(storyTeller, npc.Id, StringComparison.Ordinal))
        {
            return;
        }

        var truthFields = KnowledgeBase.BuildFields(worldEvent, false);
        var matches = true;
        foreach (var pair in existing.Fields)
        {
            if (!truthFields.TryGetValue(pair.Key, out var truthValue))
            {
                continue;
            }
            if (pair.Value is "невідомий" or "хтось")
            {
                continue;
            }
            if (!string.Equals(pair.Value, truthValue, StringComparison.Ordinal))
            {
                matches = false;
                break;
            }
        }

        if (world.Npc(storyTeller) is null && !string.Equals(storyTeller, Player.PlayerId, StringComparison.Ordinal))
        {
            return;
        }

        world.Trust.Adjust(npc.Id, storyTeller,
            matches ? world.Config.TrustUpConfirm : -world.Config.TrustDownContradict);
    }

    /// <summary>Кого NPC видел за последние игровые часы — нужно для искажения поля actor.</summary>
    public void UpdateSeen(World world)
    {
        foreach (var npc in world.ActiveNpcs())
        {
            if (npc.IsAsleep)
            {
                continue;
            }
            var radius = world.Config.VisionRadius;
            if (world.Clock.IsNight(world.Config))
            {
                radius *= world.Config.NightVisionFactor;
            }

            foreach (var other in world.ActiveNpcs())
            {
                if (ReferenceEquals(other, npc))
                {
                    continue;
                }
                var distance = Pathfinding.Euclidean(npc.Position, other.Position);
                if (distance > radius)
                {
                    continue;
                }
                if (!LineOfSight.IsClear(world.Map, npc.Position, other.Position))
                {
                    continue;
                }
                npc.SeenNpcAtTick[other.Id] = world.Tick;
            }

            var playerDistance = Pathfinding.Euclidean(npc.Position, world.Player.TileOf);
            if (playerDistance <= radius && LineOfSight.IsClear(world.Map, npc.Position, world.Player.TileOf))
            {
                npc.SeenNpcAtTick[Player.PlayerId] = world.Tick;
            }
        }
    }
}
