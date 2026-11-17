using Svidok.Core.Entities;

namespace Svidok.Core.Decisions.Handlers;

/// <summary>go_to — идёт в зону.</summary>
public sealed class GoToActionHandler : TravelActionHandler
{
    public override string ActionId => "go_to";

    protected override (int X, int Y)? PickDestination(ActionContext ctx)
    {
        var zoneId = ctx.Action.ZoneId;
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            zoneId = ctx.Npc.WorkZone;
        }
        return ZoneTile(ctx, zoneId) ?? ctx.Npc.Position;
    }
}

/// <summary>investigate — идёт к месту события и стоит 10 тиков.</summary>
public sealed class InvestigateActionHandler : TravelActionHandler
{
    public override string ActionId => "investigate";

    protected override int DwellTicks => 10;

    protected override (int X, int Y)? PickDestination(ActionContext ctx)
    {
        var entry = ctx.Action.EventRef is int eventRef ? ctx.Npc.Knowledge.ByEventId(eventRef) : null;
        var zoneId = entry?.Field("zone") ?? ctx.Action.ZoneId;
        if (!string.IsNullOrWhiteSpace(zoneId) && ctx.World.Zone(zoneId) is not null)
        {
            return ZoneTile(ctx, zoneId) ?? ctx.Npc.Position;
        }
        return ctx.Npc.Position;
    }

    /// <summary>В игровых сценариях проверка чем-то заканчивается: нашёл вещь, пошёл обыскивать подозреваемого или признал, что ничего нет.</summary>
    protected override void OnDwellDone(ActionContext ctx, TravelState state)
    {
        if (ctx.World.Game.Social)
        {
            Svidok.Core.Scenarios.GameMechanics.ResolveInvestigation(ctx);
        }
    }
}

/// <summary>hide_valuables — идёт домой и 20 тиков прячет ценное.</summary>
public sealed class HideValuablesActionHandler : TravelActionHandler
{
    public override string ActionId => "hide_valuables";

    protected override int DwellTicks => 20;

    protected override (int X, int Y)? PickDestination(ActionContext ctx)
    {
        return ZoneTile(ctx, ctx.Npc.HomeZone) ?? ctx.Npc.Position;
    }
}

/// <summary>flee — бежит в зону со скоростью ×2.</summary>
public sealed class FleeActionHandler : TravelActionHandler
{
    public override string ActionId => "flee";

    protected override int StepTiles => 2;

    protected override (int X, int Y)? PickDestination(ActionContext ctx)
    {
        var zoneId = ctx.Action.ZoneId;
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            zoneId = ctx.Npc.HomeZone;
        }
        return ZoneTile(ctx, zoneId) ?? ctx.Npc.Position;
    }
}

/// <summary>help_fire — идёт к огню и 30 тиков тушит.</summary>
public sealed class HelpFireActionHandler : TravelActionHandler
{
    public override string ActionId => "help_fire";

    protected override int DwellTicks => 30;

    protected override (int X, int Y)? PickDestination(ActionContext ctx)
    {
        var entry = ctx.Action.EventRef is int eventRef ? ctx.Npc.Knowledge.ByEventId(eventRef) : null;
        var zoneId = entry?.Field("zone") ?? ctx.Action.ZoneId ?? "forge";
        return ZoneTile(ctx, zoneId) ?? ctx.Npc.Position;
    }
}

/// <summary>close_shop — уходит с рынка домой до 18:00.</summary>
public sealed class CloseShopActionHandler : TravelActionHandler
{
    public override string ActionId => "close_shop";

    protected override (int X, int Y)? PickDestination(ActionContext ctx)
    {
        return ZoneTile(ctx, ctx.Npc.HomeZone) ?? ctx.Npc.Position;
    }

    public override bool Tick(ActionContext ctx)
    {
        var done = base.Tick(ctx);
        if (done)
        {
            return true;
        }
        return ctx.World.Clock.MinuteOfDay >= 18 * 60;
    }
}
