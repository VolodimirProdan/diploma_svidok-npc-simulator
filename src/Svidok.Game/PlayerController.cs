using System.Numerics;
using Raylib_cs;
using Svidok.Core.Entities;
using Svidok.Core.Knowledge;
using Svidok.Core.Scenarios;
using Svidok.Core.WorldModel;

namespace Svidok.Game;

/// <summary>Управление игроком: движение WASD и действия Q/F/G (раздел 11.3).</summary>
public sealed class PlayerController
{
    private const double SpeedTilesPerSecond = 4.0;

    private readonly SimulationHost _host;

    public PlayerController(SimulationHost host)
    {
        _host = host;
    }

    public void Update(double dt)
    {
        var world = _host.Simulation.World;
        var player = world.Player;
        var tick = world.Tick;

        if (Raylib.IsKeyPressed(KeyboardKey.B) && !player.IsAsleep(tick))
        {
            _host.Sleep();
            return;
        }

        if (player.IsKnockedOut(tick) || player.IsJailed(tick) || player.WorkUntilTick > tick || player.IsAsleep(tick))
        {
            if (Raylib.IsKeyPressed(KeyboardKey.W) || Raylib.IsKeyPressed(KeyboardKey.A) ||
                Raylib.IsKeyPressed(KeyboardKey.S) || Raylib.IsKeyPressed(KeyboardKey.D))
            {
                _host.ShowNotice(player.IsKnockedOut(tick) ? "Ви без тями."
                    : player.IsAsleep(tick) ? "Ви спите до " + world.TimeOf(player.SleepUntilTick).Split(", ")[^1] + "."
                    : player.IsJailed(tick) ? "Ви в обезьяннику до " + world.TimeOf(player.JailedUntilTick).Split(", ")[^1] + "."
                    : "Ви працюєте. Ще трохи.");
            }
            return;
        }

        Move(world, player, dt);

        if (world.Game.Life && Raylib.IsKeyPressed(KeyboardKey.Z))
        {
            Eat(world, player);
        }
        if ((world.Game.Life || world.Game.Social) && Raylib.IsKeyPressed(KeyboardKey.J))
        {
            Work();
        }
        if (world.Game.Combat && Raylib.IsKeyPressed(KeyboardKey.X))
        {
            HitNearest(world, player);
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Q))
        {
            Steal(world, player);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F))
        {
            PickUpOrDrop(world, player);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.G))
        {
            GiveToNeighbour(world, player);
        }
    }

    private static void Move(World world, Player player, double dt)
    {
        var dx = 0.0;
        var dy = 0.0;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right))
        {
            dx += 1;
        }
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left))
        {
            dx -= 1;
        }
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down))
        {
            dy += 1;
        }
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up))
        {
            dy -= 1;
        }
        if (dx == 0 && dy == 0)
        {
            return;
        }

        var length = Math.Sqrt((dx * dx) + (dy * dy));
        dx /= length;
        dy /= length;
        var step = SpeedTilesPerSecond * dt;

        var nextX = player.RenderX + dx * step;
        if (world.Map.IsWalkable((int)Math.Round(nextX), (int)Math.Round(player.RenderY)))
        {
            player.RenderX = nextX;
        }
        var nextY = player.RenderY + dy * step;
        if (world.Map.IsWalkable((int)Math.Round(player.RenderX), (int)Math.Round(nextY)))
        {
            player.RenderY = nextY;
        }

        player.X = (int)Math.Round(player.RenderX);
        player.Y = (int)Math.Round(player.RenderY);
    }

    private void Steal(World world, Player player)
    {
        var npc = world.NearestNpc(player.TileOf, 1.4);
        if (npc is not null && npc.Inventory.Count > 0)
        {
            var item = npc.Inventory[0];
            npc.Inventory.RemoveAt(0);
            player.AddItem(item);
            _host.EmitEvent("theft", Player.PlayerId, npc.Id, item,
                world.ZoneOfTile(npc.X, npc.Y), npc.X, npc.Y);
            _host.ShowNotice(UiText.Stolen + ": " + world.ItemName(item));
            return;
        }

        var ground = world.GroundItems.FirstOrDefault(g => g.X == player.X && g.Y == player.Y);
        if (ground is not null)
        {
            world.GroundItems.Remove(ground);
            player.AddItem(ground.ItemId);
            _host.EmitEvent("theft", Player.PlayerId, null, ground.ItemId,
                world.ZoneOfTile(ground.X, ground.Y), ground.X, ground.Y);
            _host.ShowNotice(UiText.Stolen + ": " + world.ItemName(ground.ItemId));
            return;
        }

        _host.ShowNotice(UiText.NothingHere);
    }

    private void PickUpOrDrop(World world, Player player)
    {
        var ground = world.GroundItems.FirstOrDefault(g => g.X == player.X && g.Y == player.Y);
        if (ground is not null)
        {
            world.GroundItems.Remove(ground);
            player.AddItem(ground.ItemId);
            _host.ShowNotice(UiText.PickedUp + ": " + world.ItemName(ground.ItemId));
            return;
        }

        var selected = player.SelectedItem;
        if (selected is null)
        {
            _host.ShowNotice(UiText.NothingHere);
            return;
        }

        player.RemoveItem(selected);
        world.GroundItems.Add(new GroundItem(selected, player.X, player.Y, world.ZoneOfTile(player.X, player.Y)));
        _host.ShowNotice("Покладено: " + world.ItemName(selected));
    }

    private void Eat(World world, Player player)
    {
        var food = player.SelectedItem is { } selected && world.Items.TryGetValue(selected, out var def) && def.Food > 0
            ? selected
            : player.Inventory.FirstOrDefault(i => world.Items.TryGetValue(i, out var d) && d.Food > 0);
        if (food is null)
        {
            _host.ShowNotice("Немає чого їсти. Купіть хліб у корчмі чи на ринку.");
            return;
        }
        var name = world.ItemName(food);
        GameMechanics.PlayerEat(world, food);
        _host.ShowNotice("З'їли: " + name + ". Голод: " + (int)player.Hunger + "%.");
    }

    private void Work()
    {
        var job = GameMechanics.PlayerStartWork(_host.Simulation);
        if (job is null)
        {
            _host.ShowNotice("Тут немає роботи. Де наймають — O, розділ «Як ще заробити».");
            return;
        }
        _host.ShowNotice(job.Started ? job.TitleUk + ": пів години роботи, плата " + job.Pay + " мон." : job.Message ?? string.Empty);
    }

    private void HitNearest(World world, Player player)
    {
        var npc = world.NearestNpc(player.TileOf, 1.6);
        if (npc is null)
        {
            _host.ShowNotice(UiText.NeedTarget);
            return;
        }
        GameMechanics.PlayerHit(world, npc);
        _host.ShowNotice(npc.IsKnockedOut(world.Tick)
            ? npc.Name + " без тями."
            : "Ви вдарили: " + npc.Name + ". Здоров'я: " + npc.Hp + "/" + npc.MaxHp + ".");
    }

    private void GiveToNeighbour(World world, Player player)
    {
        var npc = world.NearestNpc(player.TileOf, 1.5);
        if (npc is null)
        {
            _host.ShowNotice(UiText.NeedTarget);
            return;
        }

        var item = player.SelectedItem;
        if (item is null)
        {
            _host.ShowNotice(UiText.NothingHere);
            return;
        }

        var quest = _host.Simulation.PlayerGiveItem(item, npc);
        _host.ShowNotice(quest is null
            ? UiText.Given + ": " + world.ItemName(item)
            : "Завдання виконано: " + quest.TitleUk);
    }
}
