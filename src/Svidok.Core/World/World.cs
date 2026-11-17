using Svidok.Core.Config;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Quests;

namespace Svidok.Core.WorldModel;

/// <summary>Состояние мира: карта, персонажи, время, журнал, доверие, задания.</summary>
public sealed class World
{
    public World(
        GameMap map,
        IEnumerable<Npc> npcs,
        Player player,
        GameClock clock,
        Rng rng,
        SimulationConfig config,
        EventLog log,
        TrustMatrix trust)
    {
        Map = map;
        Npcs = npcs.ToList();
        NpcById = Npcs.ToDictionary(n => n.Id, StringComparer.Ordinal);
        Player = player;
        Clock = clock;
        Rng = rng;
        Config = config;
        Log = log;
        Schema = log.Schema;
        Trust = trust;
    }

    public GameMap Map { get; }

    public List<Npc> Npcs { get; }

    public Dictionary<string, Npc> NpcById { get; }

    public Player Player { get; }

    public GameClock Clock { get; }

    public Rng Rng { get; }

    public SimulationConfig Config { get; }

    public EventLog Log { get; }

    public EventSchema Schema { get; }

    public TrustMatrix Trust { get; }

    public QuestSystem Quests { get; } = new();

    /// <summary>Игровые механики (S6–S8): включённые функции, обмены, требования, счётчики.</summary>
    public GameState Game { get; } = new();

    public Dictionary<string, ItemDef> Items { get; } = new(StringComparer.Ordinal);

    /// <summary>Предметы, лежащие на земле (на тайлах зон).</summary>
    public List<GroundItem> GroundItems { get; } = new();

    public int Tick => Clock.Tick;

    public bool RumorsEnabled => Config.RumorsEnabled;

    public Npc? Npc(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return NpcById.TryGetValue(id, out var npc) ? npc : null;
    }

    public Zone? Zone(string? id) => Map.ZoneById(id);

    public string ZoneName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "невідомо";
        }
        var zone = Map.ZoneById(id);
        return zone?.NameUk ?? "Дорога";
    }

    public string ItemName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "щось";
        }
        return Items.TryGetValue(id, out var item) ? item.NameUk : id;
    }

    public string NpcName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "невідомий";
        }
        if (string.Equals(id, Player.PlayerId, StringComparison.Ordinal))
        {
            return Player.Name;
        }
        return NpcById.TryGetValue(id, out var npc) ? npc.Name : id;
    }

    public string CharacterName(string? id) => NpcName(id);

    public string ZoneOfTile(int x, int y) => Map.ZoneAt(x, y)?.Id ?? "road";

    /// <summary>Игровое время по тику: «День 1, 14:35».</summary>
    public string TimeOf(int tick)
    {
        var absolute = Clock.StartMinuteOfDay + tick;
        var day = absolute / GameClock.TicksPerDay + 1;
        var minute = absolute % GameClock.TicksPerDay;
        return "День " + day + ", " + (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
    }

    /// <summary>Бодрствующие NPC, которые сейчас в мире (Захар появляется по сценарию).</summary>
    public IEnumerable<Npc> ActiveNpcs()
    {
        foreach (var npc in Npcs)
        {
            if (npc.SpawnTick > Tick)
            {
                continue;
            }
            yield return npc;
        }
    }

    public List<Npc> NpcsInRadius((int X, int Y) position, double radius, Npc? except = null)
    {
        var result = new List<Npc>();
        foreach (var npc in ActiveNpcs())
        {
            if (ReferenceEquals(npc, except) || npc.SpawnTick > Tick)
            {
                continue;
            }
            if (Pathfinding.Euclidean(npc.Position, position) <= radius)
            {
                result.Add(npc);
            }
        }
        return result;
    }

    public Npc? NearestNpc((int X, int Y) position, double radius, Npc? except = null)
    {
        Npc? best = null;
        var bestDistance = double.MaxValue;
        foreach (var npc in ActiveNpcs())
        {
            if (ReferenceEquals(npc, except))
            {
                continue;
            }
            var distance = Pathfinding.Euclidean(npc.Position, position);
            if (distance <= radius && distance < bestDistance)
            {
                best = npc;
                bestDistance = distance;
            }
        }
        return best;
    }
}
