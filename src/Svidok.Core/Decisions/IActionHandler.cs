using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

public sealed class ActionContext
{
    public ActionContext(World world, Npc npc, ActionInstance action)
    {
        World = world;
        Npc = npc;
        Action = action;
    }

    public World World { get; }

    public Npc Npc { get; }

    public ActionInstance Action { get; }

    public GameMap Map => World.Map;

    public int Tick => World.Tick;
}

/// <summary>Обработчик одного типа действия (раздел 9.9).</summary>
public interface IActionHandler
{
    string ActionId { get; }

    void Start(ActionContext ctx);

    /// <summary>Возвращает true, когда действие завершено.</summary>
    bool Tick(ActionContext ctx);
}

public static class NpcMovement
{
    /// <summary>Идёт к цели по A*, шаг за тик. Возвращает true, если цель достигнута.</summary>
    public static bool MoveTowards(World world, Npc npc, (int X, int Y) goal, int stepTiles = 1)
    {
        if (npc.Position == goal)
        {
            return true;
        }

        var target = Pathfinding.NearestWalkable(world.Map, goal);
        if (npc.Path.Count == 0)
        {
            var path = Pathfinding.FindPath(world.Map, npc.Position, target);
            if (path is null)
            {
                return true;
            }
            foreach (var tile in path)
            {
                npc.Path.Enqueue(tile);
            }
        }

        for (var i = 0; i < stepTiles && npc.Path.Count > 0; i++)
        {
            var next = npc.Path.Dequeue();
            if (world.Map.IsWalkable(next.X, next.Y))
            {
                npc.X = next.X;
                npc.Y = next.Y;
            }
        }

        return npc.X == target.X && npc.Y == target.Y;
    }

    public static bool IsAt(Npc npc, (int X, int Y) goal, double tolerance = 1.2) =>
        Pathfinding.Euclidean(npc.Position, goal) <= tolerance;
}

/// <summary>Базовый обработчик «дойти до места и подождать».</summary>
public abstract class TravelActionHandler : IActionHandler
{
    protected sealed class TravelState
    {
        public (int X, int Y) Destination { get; set; }

        public bool Arrived { get; set; }

        public int ArriveTick { get; set; }

        public bool Started { get; set; }
    }

    public abstract string ActionId { get; }

    protected virtual int DwellTicks => 0;

    protected virtual int StepTiles => 1;

    protected virtual (int X, int Y)? PickDestination(ActionContext ctx) => null;

    protected virtual void OnArrive(ActionContext ctx, TravelState state)
    {
    }

    protected virtual void OnDwellDone(ActionContext ctx, TravelState state)
    {
    }

    public void Start(ActionContext ctx)
    {
        var state = new TravelState();
        var destination = PickDestination(ctx);
        if (destination is null)
        {
            state.Arrived = true;
            state.ArriveTick = ctx.Tick;
            ctx.Action.State = state;
            OnArrive(ctx, state);
            return;
        }
        state.Destination = destination.Value;
        ctx.Action.State = state;
        ctx.Npc.ClearPath();
    }

    public virtual bool Tick(ActionContext ctx)
    {
        var state = ctx.Action.State as TravelState;
        if (state is null)
        {
            Start(ctx);
            state = (TravelState)ctx.Action.State!;
        }

        if (!state.Started)
        {
            state.Started = true;
        }

        if (!state.Arrived)
        {
            var reached = NpcMovement.MoveTowards(ctx.World, ctx.Npc, state.Destination, StepTiles);
            if (!reached)
            {
                return false;
            }
            state.Arrived = true;
            state.ArriveTick = ctx.Tick;
            OnArrive(ctx, state);
            if (DwellTicks <= 0)
            {
                OnDwellDone(ctx, state);
                return true;
            }
            return false;
        }

        if (ctx.Tick - state.ArriveTick >= DwellTicks)
        {
            OnDwellDone(ctx, state);
            return true;
        }
        return false;
    }

    protected static (int X, int Y)? ZoneTile(ActionContext ctx, string? zoneId)
    {
        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return null;
        }
        var zone = ctx.World.Zone(zoneId);
        if (zone is null)
        {
            return null;
        }
        return ctx.Map.RandomTileIn(zoneId, ctx.World.Rng.Sim, ctx.Npc.Position);
    }
}
