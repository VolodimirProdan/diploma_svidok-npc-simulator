using Svidok.Core.Entities;
using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions.Handlers;

/// <summary>warn — идёт к цели и передаёт запись как разговор, без случайности.</summary>
public sealed class WarnActionHandler : IActionHandler
{
    public string ActionId => "warn";

    public void Start(ActionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Action.TargetId) &&
            ctx.Action.EventRef is int eventRef)
        {
            var entry = ctx.Npc.Knowledge.ByEventId(eventRef);
            var nearest = FindNearestOther(ctx, entry);
            ctx.Action.TargetId = nearest?.Id;
        }
        ctx.Npc.ClearPath();
    }

    public bool Tick(ActionContext ctx)
    {
        var target = ctx.World.Npc(ctx.Action.TargetId);
        if (target is null)
        {
            return true;
        }

        var reached = NpcMovement.MoveTowards(ctx.World, ctx.Npc, target.Position);
        if (!reached && !NpcMovement.IsAt(ctx.Npc, target.Position, 1.6))
        {
            return false;
        }

        TransferEntry(ctx, target);
        return true;
    }

    internal static void TransferEntry(ActionContext ctx, Npc target)
    {
        if (ctx.Action.EventRef is not int eventRef)
        {
            return;
        }
        var entry = ctx.Npc.Knowledge.ByEventId(eventRef);
        if (entry is null)
        {
            return;
        }

        var trust = ctx.World.Trust.Get(target.Id, ctx.Npc.Id);
        var credibility = Credibility.Transfer(entry.C, trust);
        if (credibility < ctx.World.Config.CForget)
        {
            return;
        }

        var copy = entry.Clone();
        copy.C = credibility;
        copy.Source = Source.Rumor;
        copy.Chain = new List<string>(entry.Chain) { target.Id };
        copy.LearnedTick = ctx.Tick;
        copy.UpdatedTick = ctx.Tick;
        copy.SharedWith = new HashSet<string>(StringComparer.Ordinal);
        target.Knowledge.MergeIncoming(copy, ctx.Tick);
        entry.SharedWith.Add(target.Id);
    }

    private static Npc? FindNearestOther(ActionContext ctx, KnowledgeEntry? entry)
    {
        Npc? best = null;
        var bestDistance = double.MaxValue;
        foreach (var npc in ctx.World.ActiveNpcs())
        {
            if (ReferenceEquals(npc, ctx.Npc))
            {
                continue;
            }
            if (entry is not null && entry.Fields.TryGetValue("target", out var targetId) &&
                string.Equals(npc.Id, targetId, StringComparison.Ordinal))
            {
                return npc;
            }
            var distance = Pathfinding.Euclidean(npc.Position, ctx.Npc.Position);
            if (distance < bestDistance)
            {
                best = npc;
                bestDistance = distance;
            }
        }
        return best;
    }
}

/// <summary>report_to_guard — предупреждает ближайшего вартового.</summary>
public sealed class ReportToGuardActionHandler : IActionHandler
{
    public string ActionId => "report_to_guard";

    public void Start(ActionContext ctx)
    {
        var guard = NearestGuard(ctx);
        ctx.Action.TargetId = guard?.Id;
        ctx.Npc.ClearPath();
    }

    public bool Tick(ActionContext ctx)
    {
        var guard = ctx.World.Npc(ctx.Action.TargetId) ?? NearestGuard(ctx);
        if (guard is null)
        {
            return true;
        }
        ctx.Action.TargetId = guard.Id;

        var reached = NpcMovement.MoveTowards(ctx.World, ctx.Npc, guard.Position);
        if (!reached && !NpcMovement.IsAt(ctx.Npc, guard.Position, 1.6))
        {
            return false;
        }

        WarnActionHandler.TransferEntry(ctx, guard);
        if (ctx.World.Game.Social)
        {
            Svidok.Core.Scenarios.GameMechanics.GuardFollowUp(ctx.World, guard, ctx.Action.EventRef);
        }
        return true;
    }

    private static Npc? NearestGuard(ActionContext ctx)
    {
        Npc? best = null;
        var bestDistance = double.MaxValue;
        foreach (var npc in ctx.World.ActiveNpcs())
        {
            if (npc.Role != Role.Guard)
            {
                continue;
            }
            var distance = Pathfinding.Euclidean(npc.Position, ctx.Npc.Position);
            if (distance < bestDistance)
            {
                best = npc;
                bestDistance = distance;
            }
        }
        return best;
    }
}

/// <summary>shout_alarm — крик с текстом (событие shout).</summary>
public sealed class ShoutAlarmActionHandler : IActionHandler
{
    public string ActionId => "shout_alarm";

    public void Start(ActionContext ctx)
    {
        var text = string.IsNullOrWhiteSpace(ctx.Action.Say) ? "Тривога!" : ctx.Action.Say!;
        var worldEvent = ctx.World.Log.Add(
            ctx.Tick,
            "shout",
            ctx.Npc.Id,
            null,
            null,
            ctx.World.ZoneOfTile(ctx.Npc.X, ctx.Npc.Y),
            ctx.Npc.X,
            ctx.Npc.Y,
            2.0,
            ctx.World.Schema.Importance("shout"),
            false,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = text });
        ctx.Action.EventRef = worldEvent.Id;
        ctx.Npc.LastSay = text;
        ctx.Npc.LastSayTick = ctx.Tick;
    }

    public bool Tick(ActionContext ctx) => true;
}

/// <summary>arrest — вартовий ведёт цель и задерживает её на 60 тиков.</summary>
public sealed class ArrestActionHandler : IActionHandler
{
    public string ActionId => "arrest";

    public void Start(ActionContext ctx)
    {
        ctx.Npc.ClearPath();
    }

    public bool Tick(ActionContext ctx)
    {
        if (string.Equals(ctx.Action.TargetId, Player.PlayerId, StringComparison.Ordinal))
        {
            return TickPlayer(ctx);
        }

        var target = ctx.World.Npc(ctx.Action.TargetId);
        if (target is null)
        {
            return true;
        }

        var reached = NpcMovement.MoveTowards(ctx.World, ctx.Npc, target.Position);
        if (!reached && !NpcMovement.IsAt(ctx.Npc, target.Position, 1.6))
        {
            return false;
        }

        ctx.World.Game.Arrests.Add(target.Id);
        target.StunnedUntilTick = ctx.Tick + 60;
        target.CurrentAction = null;
        target.ClearPath();
        target.Activity = Activity.React;

        ctx.World.Log.Add(
            ctx.Tick,
            "arrest",
            ctx.Npc.Id,
            target.Id,
            null,
            ctx.World.ZoneOfTile(target.X, target.Y),
            target.X,
            target.Y,
            1.2,
            ctx.World.Schema.Importance("arrest"),
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reason"] = ctx.Action.EventRef?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            });
        return true;
    }

    /// <summary>Игровые сценарии: вартовий ведёт игрока в обезьянник при вартовій вежі.</summary>
    private static bool TickPlayer(ActionContext ctx)
    {
        var world = ctx.World;
        if (!world.Game.Social || world.Player.IsJailed(ctx.Tick))
        {
            return true;
        }
        if (ctx.Tick - ctx.Action.StartTick > 60)
        {
            return true;
        }

        var position = world.Player.Position;
        var reached = NpcMovement.MoveTowards(world, ctx.Npc, position);
        if (!reached && !NpcMovement.IsAt(ctx.Npc, position, 1.6))
        {
            return false;
        }

        Scenarios.GameMechanics.JailPlayer(world, ctx.Npc.Id);
        var ev = world.Log.Add(
            ctx.Tick,
            "arrest",
            ctx.Npc.Id,
            Player.PlayerId,
            null,
            world.ZoneOfTile(position.X, position.Y),
            position.X,
            position.Y,
            1.2,
            world.Schema.Importance("arrest"),
            false,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reason"] = ctx.Action.EventRef?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            });
        ctx.Npc.Knowledge.AddFirstHand(ev, Source.Participant, 1.0, false, ctx.Tick, ctx.Npc.Id);
        return true;
    }
}
