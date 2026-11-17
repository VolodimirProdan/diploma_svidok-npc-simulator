using Svidok.Core.Entities;
using Svidok.Core.Perception;
using Svidok.Core.WorldModel;
using Xunit;

namespace Svidok.Tests;

public sealed class PerceptionTests
{
    private static string[] MapWithWall(int wallX, int wallY)
    {
        var rows = TestWorld.EmptyMap();
        var line = rows[wallY].ToCharArray();
        line[wallX] = '#';
        rows[wallY] = new string(line);
        return rows;
    }

    private static World BuildWorld(int wallX, int wallY, Npc npc)
    {
        var rows = MapWithWall(wallX, wallY);
        return TestWorld.Build(rows, npc);
    }

    [Fact]
    public void Wall_Blocks_Line_Of_Sight()
    {
        var npc = TestWorld.Npc("a", 5, 5);
        var world = BuildWorld(5, 8, npc);
        Assert.False(LineOfSight.IsClear(world.Map, (5, 5), (5, 12)));
    }

    [Fact]
    public void Quiet_Sound_Does_Not_Reach_Through_Wall()
    {
        var npc = TestWorld.Npc("a", 5, 5);
        var world = BuildWorld(5, 7, npc);
        var shout = world.Log.Add(0, "shout", "b", null, null, "market", 5, 9, 1.0, 0.6, false);

        var perception = new PerceptionSystem(world.Schema);
        perception.Process(world, new[] { shout });

        Assert.Null(npc.Knowledge.ByEventId(shout.Id));
    }

    [Fact]
    public void Loud_Sound_Is_Heard_Through_Wall_With_Unknown_Actor()
    {
        var npc = TestWorld.Npc("a", 5, 5);
        var world = BuildWorld(5, 7, npc);
        var shout = world.Log.Add(0, "shout", "b", null, null, "market", 5, 10, 2.0, 0.6, false);

        var perception = new PerceptionSystem(world.Schema);
        perception.Process(world, new[] { shout });

        var entry = npc.Knowledge.ByEventId(shout.Id);
        Assert.NotNull(entry);
        Assert.Equal(0.8, Math.Round(entry!.C, 4));
        Assert.Equal("невідомий", entry.Field("actor"));
    }

    [Fact]
    public void Sleeping_Npc_Ignores_Quiet_Event()
    {
        var npc = TestWorld.Npc("a", 5, 5, Activity.Sleep);
        var world = TestWorld.Build(TestWorld.EmptyMap(), npc);
        var shout = world.Log.Add(0, "shout", "b", null, null, "market", 5, 6, 1.0, 0.6, false);

        var perception = new PerceptionSystem(world.Schema);
        perception.Process(world, new[] { shout });

        Assert.Null(npc.Knowledge.ByEventId(shout.Id));
    }

    [Fact]
    public void Witness_Sees_Event_In_Front()
    {
        var npc = TestWorld.Npc("a", 5, 5);
        var world = TestWorld.Build(TestWorld.EmptyMap(), npc);
        var theft = world.Log.Add(0, "theft", "b", "c", "silver_cup", "market", 8, 5);

        var perception = new PerceptionSystem(world.Schema);
        perception.Process(world, new[] { theft });

        var entry = npc.Knowledge.ByEventId(theft.Id);
        Assert.NotNull(entry);
        Assert.Equal(1.0, entry!.C);
        Assert.Equal("b", entry.Field("actor"));
    }
}
