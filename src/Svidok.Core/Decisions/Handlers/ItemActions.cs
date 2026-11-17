using Svidok.Core.Entities;

namespace Svidok.Core.Decisions.Handlers;

/// <summary>give_item — передаёт предмет (событие give_item).</summary>
public sealed class GiveItemActionHandler : IActionHandler
{
    public string ActionId => "give_item";

    public void Start(ActionContext ctx)
    {
        var item = ctx.Action.ItemId;
        if (string.IsNullOrWhiteSpace(item) || !ctx.Npc.Inventory.Remove(item))
        {
            return;
        }

        var targetId = ctx.Action.TargetId;
        if (string.Equals(targetId, Player.PlayerId, StringComparison.Ordinal))
        {
            ctx.World.Player.AddItem(item);
        }
        else if (ctx.World.Npc(targetId) is Npc receiver)
        {
            receiver.Inventory.Add(item);
        }

        ctx.World.Log.Add(
            ctx.Tick,
            "give_item",
            ctx.Npc.Id,
            targetId,
            item,
            ctx.World.ZoneOfTile(ctx.Npc.X, ctx.Npc.Y),
            ctx.Npc.X,
            ctx.Npc.Y,
            0.5,
            ctx.World.Schema.Importance("give_item"),
            false);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>give_reward — выдаёт награду за выполненное задание (событие reward_given).</summary>
public sealed class GiveRewardActionHandler : IActionHandler
{
    public string ActionId => "give_reward";

    public void Start(ActionContext ctx)
    {
        var quest = ctx.World.Quests.Get(ctx.Action.QuestId);
        if (quest is null)
        {
            return;
        }

        // В V4 сюда попадает только проверенное валидатором действие (задание выполнено).
        // В V1–V3 валидатора нет, и награда выдаётся так, как её выбрала модель — это и измеряет S3.
        ctx.World.Quests.TryReward(quest.Id);

        ctx.World.Player.Coins += quest.RewardCoins;
        ctx.World.Log.Add(
            ctx.Tick,
            "reward_given",
            ctx.Npc.Id,
            Player.PlayerId,
            null,
            ctx.World.ZoneOfTile(ctx.Npc.X, ctx.Npc.Y),
            ctx.Npc.X,
            ctx.Npc.Y,
            0.5,
            ctx.World.Schema.Importance("reward_given"),
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["questId"] = quest.Id,
                ["coins"] = quest.RewardCoins.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        ctx.Npc.LastSay = string.IsNullOrWhiteSpace(ctx.Action.Say)
            ? "Ось твоя нагорода, " + quest.RewardCoins + " монет."
            : ctx.Action.Say!;
        ctx.Npc.LastSayTick = ctx.Tick;
    }

    public bool Tick(ActionContext ctx) => true;
}
