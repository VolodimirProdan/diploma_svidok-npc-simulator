using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;
using Xunit;

namespace Svidok.Tests;

public sealed class CredibilityTests
{
    [Fact]
    public void Chain_Of_Retellings_Multiplies_Trust()
    {
        var c = Credibility.Transfer(1.0, 0.8);
        c = Credibility.Transfer(c, 0.6);
        Assert.Equal(0.48, Math.Round(c, 4));
    }

    [Fact]
    public void Noisy_Or_For_Independent_Sources()
    {
        Assert.Equal(0.74, Math.Round(Credibility.NoisyOr(0.48, 0.5), 4));
    }

    [Fact]
    public void Forgetting_Reduces_Credibility()
    {
        var c = Credibility.Forget(0.5, 0.02, 10);
        Assert.InRange(c, 0.4090, 0.4098);
    }

    [Fact]
    public void Same_Origin_Keeps_Maximum()
    {
        var world = TestWorld.Build(TestWorld.EmptyMap(), TestWorld.Npc("a", 1, 1));
        var npc = world.Npcs[0];
        var ev = world.Log.Add(0, "theft", "x", "y", "silver_cup", "market", 2, 2);

        var first = npc.Knowledge.AddFirstHand(ev, Source.Rumor, 0.48, false, 0, "marta");
        var second = first.Clone();
        second.C = 0.3;
        second.OriginId = "marta";
        second.Chain = new List<string> { "marta", "a" };

        var merged = npc.Knowledge.MergeIncoming(second, 5);
        Assert.Equal(0.48, Math.Round(merged!.C, 4));
    }

    [Fact]
    public void Different_Origin_Confirms_By_Noisy_Or()
    {
        var world = TestWorld.Build(TestWorld.EmptyMap(), TestWorld.Npc("a", 1, 1));
        var npc = world.Npcs[0];
        var ev = world.Log.Add(0, "theft", "x", "y", "silver_cup", "market", 2, 2);

        var first = npc.Knowledge.AddFirstHand(ev, Source.Rumor, 0.48, false, 0, "marta");
        first.Chain = new List<string> { "marta", "a" };

        var incoming = first.Clone();
        incoming.C = 0.5;
        incoming.OriginId = "ostap";
        incoming.Chain = new List<string> { "ostap", "a" };

        var merged = npc.Knowledge.MergeIncoming(incoming, 5);
        Assert.Equal(0.74, Math.Round(merged!.C, 4));
    }

    [Fact]
    public void Below_Forget_Threshold_Is_Not_Stored()
    {
        var a = TestWorld.Npc("a", 1, 1);
        var b = TestWorld.Npc("b", 1, 2);
        var world = TestWorld.Build(TestWorld.EmptyMap(), a, b);
        world.Trust.Set(b.Id, a.Id, 0.8);

        var ev = world.Log.Add(0, "theft", "x", "y", "silver_cup", "market", 2, 2);
        var entry = a.Knowledge.AddFirstHand(ev, Source.Witnessed, 0.05, false, 0, a.Id);

        var conversations = new ConversationSystem(world.Config, new DistortionEngine(world.Config, world.Schema));
        conversations.Deliver(world, a, b, entry, Source.Rumor, 1.0);

        Assert.Null(b.Knowledge.ByEventId(ev.Id));
    }
}
