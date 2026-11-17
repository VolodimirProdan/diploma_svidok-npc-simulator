using System.Globalization;
using Svidok.Core.Entities;
using Svidok.Core.Knowledge;
using Svidok.Core.Scenarios;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions.Handlers;

/// <summary>end_talk — NPC обрывает разговор и полчаса не хочет говорить с игроком.</summary>
public sealed class EndTalkActionHandler : IActionHandler
{
    public string ActionId => "end_talk";

    public void Start(ActionContext ctx)
    {
        ctx.Npc.RefuseTalkUntilTick = ctx.Tick + GameRules.RefuseTalkTicks;
        ctx.Action.Done = true;
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>demand_payment — NPC называет цену; игрок решает кнопками «Заплатити» или «Відмовитись».</summary>
public sealed class DemandPaymentActionHandler : IActionHandler
{
    public string ActionId => "demand_payment";

    public void Start(ActionContext ctx)
    {
        var coins = Math.Clamp(ctx.Action.Amount ?? 1, 1, GameRules.MaxDemandCoins);
        var item = ctx.Action.ItemId;
        if (item is not null && !ctx.Npc.Inventory.Contains(item, StringComparer.Ordinal))
        {
            item = null;
        }
        ctx.World.Game.Demands[ctx.Npc.Id] = new PaymentDemand(coins, item, ctx.Tick);
        ctx.Action.Done = true;
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>
/// keep_secret — NPC обещает молчать о событии. Сила обещания растёт с недавней платой и доверием;
/// система слухов перестаёт передавать это событие, пока обещание не ослабнет.
/// </summary>
public sealed class KeepSecretActionHandler : IActionHandler
{
    public string ActionId => "keep_secret";

    public void Start(ActionContext ctx)
    {
        ctx.Action.Done = true;
        if (ctx.Action.EventRef is not int eventRef || ctx.Npc.Knowledge.ByEventId(eventRef) is null)
        {
            return;
        }

        var paid = ctx.World.Game.PaidRecently(ctx.Npc.Id, ctx.Tick, GameRules.PaymentWindowTicks);
        var trust = ctx.World.Trust.Get(ctx.Npc.Id, Player.PlayerId);
        var strength = 0.4 + Math.Min(0.4, paid * 0.08) + (trust - 0.5) * 0.4;
        ctx.Npc.Secrets[eventRef] = Math.Clamp(strength, 0.1, 0.95);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>treat — угощает игрока: отдаёт свою еду или хлеб.</summary>
public sealed class TreatActionHandler : IActionHandler
{
    public string ActionId => "treat";

    public void Start(ActionContext ctx)
    {
        ctx.Action.Done = true;
        var world = ctx.World;
        var food = ctx.Npc.Inventory.FirstOrDefault(i => world.Items.TryGetValue(i, out var def) && def.Food > 0);
        if (food is not null)
        {
            ctx.Npc.Inventory.Remove(food);
        }
        else if (world.Items.ContainsKey("bread"))
        {
            food = "bread";
        }
        if (food is null)
        {
            return;
        }

        world.Player.AddItem(food);
        var ev = GameMechanics.Emit(world, "give_item", ctx.Npc.Id, Player.PlayerId, food, ctx.Npc.X, ctx.Npc.Y);
        ctx.Npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, ctx.Tick, ctx.Npc.Id);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>give_quest — NPC сам доручает игроку завдання (без кнопки).</summary>
public sealed class GiveQuestActionHandler : IActionHandler
{
    public string ActionId => "give_quest";

    public void Start(ActionContext ctx)
    {
        ctx.Action.Done = true;
        var world = ctx.World;
        var quest = world.Quests.Get(ctx.Action.QuestId) ?? world.Quests.QuestOfGiver(ctx.Npc.Id);
        if (quest is null || !world.Quests.TryTake(quest.Id))
        {
            return;
        }
        GameMechanics.OnQuestTaken(world, quest, ctx.Npc);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>accept_trade — NPC соглашается на обмен, предложенный игроком.</summary>
public sealed class AcceptTradeActionHandler : IActionHandler
{
    public string ActionId => "accept_trade";

    public void Start(ActionContext ctx)
    {
        ctx.Action.Done = true;
        var world = ctx.World;
        if (!world.Game.Trades.TryGetValue(ctx.Npc.Id, out var offer))
        {
            return;
        }
        var npcItem = ctx.Action.ItemId ?? offer.NpcItem;
        if (npcItem is null || !ctx.Npc.Inventory.Contains(npcItem, StringComparer.Ordinal) ||
            !world.Player.HasItem(offer.PlayerItem))
        {
            return;
        }

        world.Player.RemoveItem(offer.PlayerItem);
        ctx.Npc.Inventory.Remove(npcItem);
        ctx.Npc.Inventory.Add(offer.PlayerItem);
        world.Player.AddItem(npcItem);
        world.Game.Trades.Remove(ctx.Npc.Id);

        var ev = GameMechanics.Emit(world, "trade", Player.PlayerId, ctx.Npc.Id, offer.PlayerItem, ctx.Npc.X, ctx.Npc.Y,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["got"] = npcItem });
        ctx.Npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, ctx.Tick, ctx.Npc.Id);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>banish — вартовий или староста выгоняет игрока за околицу на 4 часа.</summary>
public sealed class BanishActionHandler : IActionHandler
{
    public string ActionId => "banish";

    public void Start(ActionContext ctx)
    {
        ctx.Action.Done = true;
        if (!string.Equals(ctx.Action.TargetId, Player.PlayerId, StringComparison.Ordinal))
        {
            return;
        }
        var world = ctx.World;
        GameMechanics.BanishPlayer(world);
        var ev = GameMechanics.Emit(world, "banish", ctx.Npc.Id, Player.PlayerId, null, ctx.Npc.X, ctx.Npc.Y);
        ctx.Npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, ctx.Tick, ctx.Npc.Id);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>fine — вартовий забирает у игрока монеты.</summary>
public sealed class FineActionHandler : IActionHandler
{
    public string ActionId => "fine";

    public void Start(ActionContext ctx)
    {
        ctx.Action.Done = true;
        if (!string.Equals(ctx.Action.TargetId, Player.PlayerId, StringComparison.Ordinal))
        {
            return;
        }
        var world = ctx.World;
        var amount = Math.Clamp(ctx.Action.Amount ?? 2, 1, GameRules.MaxDemandCoins);
        var taken = Math.Min(amount, world.Player.Coins);
        world.Player.Coins -= taken;
        ctx.Npc.Coins += taken;
        var ev = GameMechanics.Emit(world, "fine", ctx.Npc.Id, Player.PlayerId, null, ctx.Npc.X, ctx.Npc.Y,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["coins"] = taken.ToString(CultureInfo.InvariantCulture) });
        ctx.Npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, ctx.Tick, ctx.Npc.Id);
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>attack — подходит к цели и бьёт (минус 2–4 здоровья).</summary>
public sealed class AttackActionHandler : IActionHandler
{
    public string ActionId => "attack";

    public void Start(ActionContext ctx)
    {
        ctx.Npc.ClearPath();
    }

    public bool Tick(ActionContext ctx)
    {
        var world = ctx.World;
        var targetId = ctx.Action.TargetId;
        (int X, int Y) position;
        if (string.Equals(targetId, Player.PlayerId, StringComparison.Ordinal))
        {
            position = world.Player.Position;
        }
        else if (world.Npc(targetId) is Npc target)
        {
            position = target.Position;
        }
        else
        {
            return true;
        }

        if (ctx.Tick - ctx.Action.StartTick > 40)
        {
            return true;
        }

        var reached = NpcMovement.MoveTowards(world, ctx.Npc, position);
        if (!reached && !NpcMovement.IsAt(ctx.Npc, position, 1.6))
        {
            return false;
        }

        var ev = GameMechanics.Hit(world, ctx.Npc.Id, targetId!);
        if (ev is not null)
        {
            ctx.Npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, ctx.Tick, ctx.Npc.Id);
        }
        return true;
    }
}

/// <summary>
/// search — подходит к подозреваемому (игроку или жителю) и обыскивает. Нашёл вещь — забирает владельцу
/// и зовёт варту; не нашёл — признаёт, что ошибся. Сдаётся через час, если не догнал.
/// </summary>
public sealed class SearchActionHandler : IActionHandler
{
    private const int GiveUpTicks = 60;

    public string ActionId => "search";

    public void Start(ActionContext ctx)
    {
        ctx.Npc.ClearPath();
    }

    public bool Tick(ActionContext ctx)
    {
        var world = ctx.World;
        var targetId = ctx.Action.TargetId;
        (int X, int Y) position;
        if (string.Equals(targetId, Player.PlayerId, StringComparison.Ordinal))
        {
            if (world.Player.IsBanished(ctx.Tick))
            {
                return true;
            }
            position = world.Player.Position;
        }
        else if (world.Npc(targetId) is Npc target && !ReferenceEquals(target, ctx.Npc))
        {
            position = target.Position;
        }
        else
        {
            return true;
        }

        if (ctx.Tick - ctx.Action.StartTick > GiveUpTicks)
        {
            ctx.Npc.LastSay = Svidok.Core.Util.UkGrammar.For(ctx.Npc, "Не наздогнав. Пізніше розберуся.");
            ctx.Npc.LastSayTick = ctx.Tick;
            return true;
        }

        if (!NpcMovement.IsAt(ctx.Npc, position, 1.6))
        {
            ctx.Npc.ClearPath();
            NpcMovement.MoveTowards(world, ctx.Npc, position);
            if (!NpcMovement.IsAt(ctx.Npc, position, 1.6))
            {
                return false;
            }
        }

        GameMechanics.Search(world, ctx.Npc, targetId!, ctx.Action.EventRef, ctx.Action.ItemId);
        return true;
    }
}

/// <summary>
/// approach_player — житель сам идёт к игроку. Дошёл — встаёт в очередь на разговор, и окно разговора
/// открывается у игрока. Через полтора часа сдаётся.
/// </summary>
public sealed class ApproachPlayerActionHandler : IActionHandler
{
    private const int GiveUpTicks = 90;

    public string ActionId => "approach_player";

    public void Start(ActionContext ctx)
    {
        ctx.Npc.ClearPath();
        ctx.World.Game.LastApproach[ctx.Npc.Id] = ctx.Tick;
        ctx.World.Game.LastAnyApproachTick = ctx.Tick;
    }

    public bool Tick(ActionContext ctx)
    {
        var world = ctx.World;
        var player = world.Player;
        if (!world.Game.PlayerActive || player.IsBanished(ctx.Tick) || player.IsJailed(ctx.Tick) ||
            ctx.Tick - ctx.Action.StartTick > GiveUpTicks)
        {
            return true;
        }
        if (!NpcMovement.IsAt(ctx.Npc, player.Position, 1.6))
        {
            ctx.Npc.ClearPath();
            NpcMovement.MoveTowards(world, ctx.Npc, player.Position);
            if (!NpcMovement.IsAt(ctx.Npc, player.Position, 1.6))
            {
                return false;
            }
        }
        if (world.Game.IncomingTalks.Any(t => string.Equals(t.NpcId, ctx.Npc.Id, StringComparison.Ordinal)))
        {
            return true;
        }
        var opening = string.IsNullOrWhiteSpace(ctx.Action.Say) ? "Слухай, є розмова." : ctx.Action.Say!;
        world.Game.IncomingTalks.Enqueue(new IncomingTalk(ctx.Npc.Id, ctx.Action.Reason, opening, ctx.Tick));
        return true;
    }
}
