using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Knowledge;
using Svidok.Core.Quests;
using Svidok.Core.WorldModel;
using Xunit;

namespace Svidok.Tests;

public sealed class ValidatorTests
{
    private static (Validator Validator, World World) Build()
    {
        var guard = TestWorld.Npc("andrii", 10, 10, Activity.Work, Role.Guard);
        var cook = TestWorld.Npc("bohdan", 11, 10, Activity.Work, Role.Innkeeper);
        var world = TestWorld.Build(TestWorld.EmptyMap(), guard, cook);
        world.Quests.Add(new Quest("q_chest", "bohdan", "Принести скриню", "chest", 10, QuestState.Taken));
        var catalog = ActionCatalog.LoadDefault();
        return (new Validator(world, catalog), world);
    }

    [Fact]
    public void Reward_Without_Completed_Quest_Is_Rejected()
    {
        var (validator, world) = Build();
        var cook = world.Npc("bohdan")!;

        var validation = validator.Validate(cook, new LlmDecision("give_reward", QuestId: "q_chest", Target: "player"));

        Assert.False(validation.Ok);
        Assert.Equal(RejectReason.NoQuestCompletion, validation.Reason);
    }

    [Fact]
    public void Unknown_Event_Reference_Is_Rejected()
    {
        var (validator, world) = Build();
        var guard = world.Npc("andrii")!;

        var validation = validator.Validate(guard, new LlmDecision("investigate", EventRef: 999));

        Assert.False(validation.Ok);
        Assert.Equal(RejectReason.UnknownEvent, validation.Reason);
    }

    [Fact]
    public void Low_Credibility_Arrest_Is_Rejected()
    {
        var (validator, world) = Build();
        var guard = world.Npc("andrii")!;
        var thief = world.Npc("bohdan")!;
        var ev = world.Log.Add(0, "theft", thief.Id, null, "silver_cup", "market", 10, 11);
        guard.Knowledge.AddFirstHand(ev, Source.Rumor, 0.48, false, 0, "marta");

        var validation = validator.Validate(guard, new LlmDecision("arrest", Target: thief.Id, EventRef: ev.Id));

        Assert.False(validation.Ok);
        Assert.Equal(RejectReason.LowCredibility, validation.Reason);
    }

    [Fact]
    public void Non_Guard_Arrest_Is_Not_Allowed()
    {
        var (validator, world) = Build();
        var cook = world.Npc("bohdan")!;
        var ev = world.Log.Add(0, "theft", "x", null, "silver_cup", "market", 10, 11);
        cook.Knowledge.AddFirstHand(ev, Source.Witnessed, 1.0, false, 0, cook.Id);

        var validation = validator.Validate(cook, new LlmDecision("arrest", Target: "player", EventRef: ev.Id));

        Assert.False(validation.Ok);
        Assert.Equal(RejectReason.ActionNotAllowed, validation.Reason);
    }
}
