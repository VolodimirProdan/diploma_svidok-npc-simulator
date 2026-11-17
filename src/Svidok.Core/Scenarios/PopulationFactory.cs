using System.Text.Json;
using Svidok.Core.Entities;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Scenarios;

public sealed class NpcSpec
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Role { get; set; } = "Peasant";

    public string Home { get; set; } = string.Empty;

    public string Work { get; set; } = string.Empty;

    public double Talkativeness { get; set; } = 0.5;

    public string Bio { get; set; } = string.Empty;

    /// <summary>"m" или "f"; пусто — угадывается по имени.</summary>
    public string? Gender { get; set; }
}

/// <summary>Родство: кем B приходится A и наоборот («дружина» / «чоловік»).</summary>
public sealed class FamilySpec
{
    public string A { get; set; } = string.Empty;

    public string B { get; set; } = string.Empty;

    public string AIsToB { get; set; } = string.Empty;

    public string BIsToA { get; set; } = string.Empty;
}

public sealed class PopulationData
{
    public List<NpcSpec> Npcs { get; set; } = new();

    public List<NpcSpec> Extra { get; set; } = new();

    public List<string[]> Friends { get; set; } = new();

    public List<FamilySpec> Family { get; set; } = new();
}

/// <summary>Население села и расписания по ролям (раздел 13.0).</summary>
public sealed class PopulationFactory
{
    private readonly PopulationData _data;

    public PopulationFactory(PopulationData data)
    {
        _data = data;
    }

    public IReadOnlyList<(string A, string B)> Friends =>
        _data.Friends.Select(pair => (pair[0], pair[1])).ToList();

    public static PopulationData LoadData() => LoadData("default");

    /// <summary>Население: "default" — data/population.json, иначе data/population_&lt;name&gt;.json.</summary>
    public static PopulationData LoadData(string? name)
    {
        var file = string.IsNullOrWhiteSpace(name) || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase)
            ? "data/population.json"
            : "data/population_" + name.Trim() + ".json";
        var path = PathResolver.ResolveExisting(file);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Не знайдено " + file, path);
        }
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        return JsonSerializer.Deserialize<PopulationData>(File.ReadAllText(path), options)
               ?? new PopulationData();
    }

    public List<Npc> Build(IReadOnlyList<string> extraNpcIds, GameMap map, Rng rng)
    {
        var npcs = new List<Npc>();
        foreach (var spec in _data.Npcs)
        {
            npcs.Add(Create(spec, map, rng));
        }

        foreach (var id in extraNpcIds)
        {
            var spec = _data.Extra.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
            if (spec is null)
            {
                Log.Warn("Сценарій просить невідомого NPC: " + id);
                continue;
            }
            var npc = Create(spec, map, rng);
            npcs.Add(npc);
        }

        foreach (var family in _data.Family)
        {
            var a = npcs.FirstOrDefault(n => string.Equals(n.Id, family.A, StringComparison.Ordinal));
            var b = npcs.FirstOrDefault(n => string.Equals(n.Id, family.B, StringComparison.Ordinal));
            if (a is null || b is null)
            {
                continue;
            }
            a.Family[b.Id] = family.BIsToA;
            b.Family[a.Id] = family.AIsToB;
        }

        return npcs;
    }

    private static Npc Create(NpcSpec spec, GameMap map, Rng rng)
    {
        var role = Roles.Parse(spec.Role);
        var npc = new Npc(spec.Id, spec.Name, role)
        {
            Bio = spec.Bio,
            HomeZone = spec.Home,
            WorkZone = spec.Work,
            Talkativeness = spec.Talkativeness,
            IsFemale = spec.Gender is { Length: > 0 } gender
                ? gender.StartsWith("f", StringComparison.OrdinalIgnoreCase)
                : GuessFemale(spec.Name)
        };

        var home = map.RandomTileIn(spec.Home, rng.Sim);
        npc.X = home.X;
        npc.Y = home.Y;
        npc.RenderX = home.X;
        npc.RenderY = home.Y;
        npc.Activity = Activity.Sleep;

        BuildSchedule(npc, role, rng);
        return npc;
    }

    /// <summary>Украинские женские имена обычно на -а/-я (кроме известных мужских).</summary>
    private static bool GuessFemale(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "Лука" or "Микита" or "Кузьма" or "Сава" or "Ілля")
        {
            return false;
        }
        var last = char.ToLowerInvariant(name[^1]);
        return last is 'а' or 'я';
    }

    /// <summary>Расписание по роли с индивидуальным сдвигом ±20 минут (RngSim).</summary>
    private static void BuildSchedule(Npc npc, Role role, Rng rng)
    {
        var shift = rng.NextInt(-20, 21, rng.Sim);
        var home = npc.HomeZone;
        var work = npc.WorkZone;
        var rest = "tavern";

        void Add(int from, int to, string zone, Activity activity)
        {
            npc.Schedule.Add(new ScheduleEntry(from + shift, to + shift, zone, activity));
        }

        if (string.Equals(npc.Id, "andrii", StringComparison.Ordinal))
        {
            Add(0, 360, home, Activity.Sleep);
            Add(360, 390, home, Activity.Eat);
            Add(390, 1320, work, Activity.Patrol);
            Add(1320, 1440, home, Activity.Sleep);
            npc.PatrolRoute.AddRange(["square", "market", "warehouse"]);
            return;
        }

        if (string.Equals(npc.Id, "vasyl", StringComparison.Ordinal))
        {
            Add(0, 480, home, Activity.Sleep);
            Add(480, 960, home, Activity.Sleep);
            Add(960, 990, home, Activity.Eat);
            Add(990, 1200, "square", Activity.Rest);
            Add(1200, 1440, "warehouse", Activity.Patrol);
            npc.PatrolRoute.AddRange(["warehouse", "square", "tavern"]);
            return;
        }

        if (role == Role.Traveler)
        {
            Add(0, 1440, "square", Activity.Walk);
            return;
        }

        if (role is Role.Innkeeper)
        {
            Add(0, 390, home, Activity.Sleep);
            Add(390, 420, home, Activity.Eat);
            Add(420, 1410, work, Activity.Work);
            Add(1410, 1440, home, Activity.Sleep);
            return;
        }

        if (role == Role.Headman)
        {
            Add(0, 360, home, Activity.Sleep);
            Add(360, 420, home, Activity.Eat);
            Add(420, 1020, work, Activity.Work);
            Add(1020, 1320, "tavern", Activity.Rest);
            Add(1320, 1440, home, Activity.Sleep);
            return;
        }

        Add(0, 330, home, Activity.Sleep);
        Add(330, 390, home, Activity.Eat);
        Add(390, 720, work, Activity.Work);
        Add(720, 760, work, Activity.Eat);
        Add(760, 1080, work, Activity.Work);
        Add(1080, 1290, rest, Activity.Rest);
        Add(1290, 1440, home, Activity.Sleep);
    }

    /// <summary>Служница и кухарка работают в корчме, даже если роль «Селянка».</summary>
    public static void ApplySpecialSchedules(IReadOnlyList<Npc> npcs)
    {
        foreach (var npc in npcs)
        {
            if (!string.Equals(npc.WorkZone, "tavern", StringComparison.Ordinal))
            {
                continue;
            }
            npc.Schedule.Clear();
            npc.Schedule.Add(new ScheduleEntry(0, 390, npc.HomeZone, Activity.Sleep));
            npc.Schedule.Add(new ScheduleEntry(390, 420, npc.HomeZone, Activity.Eat));
            npc.Schedule.Add(new ScheduleEntry(420, 1410, "tavern", Activity.Work));
            npc.Schedule.Add(new ScheduleEntry(1410, 1440, npc.HomeZone, Activity.Sleep));
        }
    }
}
