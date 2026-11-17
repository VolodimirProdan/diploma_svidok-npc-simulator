using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Scenarios;

/// <summary>Движение NPC: расписание, патруль, действия, интерполяция для отрисовки.</summary>
public static class MovementSystem
{
    private const int PatrolStayTicks = 30;

    public static void Update(Simulation simulation)
    {
        var world = simulation.World;
        foreach (var npc in world.Npcs)
        {
            if (npc.SpawnTick > world.Tick)
            {
                continue;
            }

            if (npc.StunnedUntilTick > world.Tick)
            {
                npc.Activity = Activity.React;
                Interpolate(npc);
                continue;
            }

            if (npc.IsInConversation)
            {
                Interpolate(npc);
                continue;
            }

            if (npc.CurrentAction is not null)
            {
                simulation.Executor.Tick(npc);
                Interpolate(npc);
                continue;
            }

            ApplySchedule(world, npc);
            Interpolate(npc);
        }
    }

    private static void ApplySchedule(World world, Npc npc)
    {
        var entry = npc.CurrentScheduleEntry(world.Clock.MinuteOfDay);
        if (entry is null)
        {
            return;
        }

        // в игровых сценариях спят только в своей кровати; эксперименты S1–S5 идут как раньше
        if (entry.Activity == Activity.Sleep && world.Game.AnyEnabled &&
            world.Game.Beds.TryGetValue(npc.Id, out var bed))
        {
            GoToBed(world, npc, bed, entry.ZoneId);
            return;
        }

        npc.Activity = entry.Activity;

        if (entry.Activity == Activity.Patrol && npc.PatrolRoute.Count > 0)
        {
            UpdatePatrol(world, npc);
            return;
        }

        if (npc.Path.Count == 0)
        {
            var zone = world.Map.ZoneAt(npc.X, npc.Y);
            if (zone is not null && string.Equals(zone.Id, entry.ZoneId, StringComparison.Ordinal))
            {
                return;
            }
            BuildPathToZone(world, npc, entry.ZoneId);
        }

        MoveOne(world, npc);
    }

    /// <summary>Идёт к своей кровати; пока идёт, не спит. Если дороги нет, спит дома где стоит.</summary>
    private static void GoToBed(World world, Npc npc, (int X, int Y) bed, string homeZone)
    {
        if (npc.X == bed.X && npc.Y == bed.Y)
        {
            npc.Path.Clear();
            npc.Activity = Activity.Sleep;
            return;
        }

        if (npc.Path.Count > 0 && npc.Path.Last() != bed)
        {
            npc.Path.Clear();
        }
        if (npc.Path.Count == 0)
        {
            var path = Pathfinding.FindPath(world.Map, npc.Position, bed);
            if (path is null)
            {
                var zone = world.Map.ZoneAt(npc.X, npc.Y);
                if (zone is not null && string.Equals(zone.Id, homeZone, StringComparison.Ordinal))
                {
                    npc.Activity = Activity.Sleep;
                    return;
                }
                npc.Activity = Activity.Walk;
                BuildPathToZone(world, npc, homeZone);
                MoveOne(world, npc);
                return;
            }
            foreach (var tile in path)
            {
                npc.Path.Enqueue(tile);
            }
        }

        npc.Activity = Activity.Walk;
        MoveOne(world, npc);
        if (npc.X == bed.X && npc.Y == bed.Y)
        {
            npc.Activity = Activity.Sleep;
        }
    }

    private static void UpdatePatrol(World world, Npc npc)
    {
        var targetZoneId = npc.PatrolRoute[npc.PatrolIndex % npc.PatrolRoute.Count];
        var zone = world.Map.ZoneAt(npc.X, npc.Y);
        var inTargetZone = zone is not null && string.Equals(zone.Id, targetZoneId, StringComparison.Ordinal);

        if (inTargetZone && npc.Path.Count == 0)
        {
            if (npc.PatrolWaitUntil == 0)
            {
                npc.PatrolWaitUntil = world.Tick + PatrolStayTicks;
                return;
            }
            if (world.Tick >= npc.PatrolWaitUntil)
            {
                npc.PatrolWaitUntil = 0;
                npc.PatrolIndex = (npc.PatrolIndex + 1) % npc.PatrolRoute.Count;
                targetZoneId = npc.PatrolRoute[npc.PatrolIndex];
                BuildPathToZone(world, npc, targetZoneId);
            }
            return;
        }

        if (npc.Path.Count == 0)
        {
            BuildPathToZone(world, npc, targetZoneId);
        }
        MoveOne(world, npc);
    }

    public static void BuildPathToZone(World world, Npc npc, string zoneId)
    {
        var destination = ChooseDestination(world, npc, zoneId);
        var path = Pathfinding.FindPath(world.Map, npc.Position, destination);
        if (path is null)
        {
            return;
        }
        foreach (var tile in path)
        {
            npc.Path.Enqueue(tile);
        }
    }

    /// <summary>Люди тягнуться одне до одного: частіше стають поруч, а не в порожньому куті зони.</summary>
    private static (int X, int Y) ChooseDestination(World world, Npc npc, string zoneId)
    {
        var neighbours = new List<Npc>();
        foreach (var other in world.ActiveNpcs())
        {
            if (ReferenceEquals(other, npc))
            {
                continue;
            }
            if (!string.Equals(world.ZoneOfTile(other.X, other.Y), zoneId, StringComparison.Ordinal))
            {
                continue;
            }
            neighbours.Add(other);
        }

        if (neighbours.Count > 0 && world.Rng.Chance(0.75, world.Rng.Sim))
        {
            var anchor = neighbours[world.Rng.NextInt(0, neighbours.Count, world.Rng.Sim)];
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var x = anchor.X + world.Rng.NextInt(-2, 3, world.Rng.Sim);
                var y = anchor.Y + world.Rng.NextInt(-2, 3, world.Rng.Sim);
                if (!world.Map.IsWalkable(x, y))
                {
                    continue;
                }
                if (!string.Equals(world.ZoneOfTile(x, y), zoneId, StringComparison.Ordinal))
                {
                    continue;
                }
                return (x, y);
            }
        }

        return world.Map.RandomTileIn(zoneId, world.Rng.Sim, npc.Position);
    }

    public static void MoveOne(World world, Npc npc)
    {
        if (npc.Path.Count == 0)
        {
            return;
        }
        var next = npc.Path.Dequeue();
        if (!world.Map.IsWalkable(next.X, next.Y))
        {
            npc.Path.Clear();
            return;
        }
        var previousZone = world.ZoneOfTile(npc.X, npc.Y);
        npc.X = next.X;
        npc.Y = next.Y;

        if (npc.Path.Count == 0)
        {
            var zone = world.ZoneOfTile(npc.X, npc.Y);
            if (!string.Equals(previousZone, zone, StringComparison.Ordinal))
            {
                world.Log.Add(
                    world.Tick,
                    "arrive",
                    npc.Id,
                    null,
                    null,
                    zone,
                    npc.X,
                    npc.Y,
                    world.Schema.Get("arrive").Loudness,
                    world.Schema.Get("arrive").Importance,
                    true);
            }
        }
    }

    private static void Interpolate(Npc npc)
    {
        npc.RenderX += (npc.X - npc.RenderX) * 0.45;
        npc.RenderY += (npc.Y - npc.RenderY) * 0.45;
        if (Math.Abs(npc.RenderX - npc.X) < 0.01)
        {
            npc.RenderX = npc.X;
        }
        if (Math.Abs(npc.RenderY - npc.Y) < 0.01)
        {
            npc.RenderY = npc.Y;
        }
    }
}
