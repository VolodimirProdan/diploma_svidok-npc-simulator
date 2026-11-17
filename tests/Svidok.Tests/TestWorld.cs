using Svidok.Core.Config;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;

namespace Svidok.Tests;

/// <summary>Сборка маленького мира для unit-тестов восприятия и знаний.</summary>
internal static class TestWorld
{
    public static string[] EmptyMap(int width = 16, int height = 16)
    {
        var rows = new string[height];
        for (var y = 0; y < height; y++)
        {
            var line = new char[width];
            for (var x = 0; x < width; x++)
            {
                line[x] = '.';
            }
            rows[y] = new string(line);
        }
        return rows;
    }

    public static World Build(string[] rows, params Npc[] npcs)
    {
        var map = GameMap.FromAscii(rows, DefaultZones.All);
        var schema = EventSchema.LoadDefault();
        var config = new SimulationConfig();
        var log = new EventLog(schema);
        var rng = new Rng(1);
        var player = new Player { X = 0, Y = 0 };
        var clock = new GameClock();
        var trust = new TrustMatrix();
        var all = npcs.ToList();
        trust.Build(all, config, Array.Empty<(string, string)>(), 1.0, includePlayer: true);
        return new World(map, all, player, clock, rng, config, log, trust);
    }

    public static Npc Npc(string id, int x, int y, Activity activity = Activity.Work, Role role = Role.Peasant)
    {
        return new Npc(id, id, role)
        {
            X = x,
            Y = y,
            RenderX = x,
            RenderY = y,
            Activity = activity,
            HomeZone = "house_2",
            WorkZone = "market"
        };
    }
}
