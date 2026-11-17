using System.Numerics;
using Raylib_cs;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;
using Color = Raylib_cs.Color;

namespace Svidok.Game;

/// <summary>Отрисовка карты, персонажей и оверлеев (клавиши V, H, T, G, L, R).</summary>
public sealed class WorldRenderer
{
    private static readonly Dictionary<TileKind, Color> TileColors = new()
    {
        [TileKind.Grass] = new Color(92, 134, 68, 255),
        [TileKind.Road] = new Color(158, 142, 112, 255),
        [TileKind.Square] = new Color(178, 162, 130, 255),
        [TileKind.Floor] = new Color(150, 116, 82, 255),
        [TileKind.Wall] = new Color(70, 70, 82, 255),
        [TileKind.DoorOpen] = new Color(198, 158, 92, 255),
        [TileKind.DoorClosed] = new Color(120, 92, 58, 255),
        [TileKind.Window] = new Color(146, 194, 222, 255),
        [TileKind.Tree] = new Color(52, 98, 56, 255),
        [TileKind.Water] = new Color(72, 124, 186, 255),
        [TileKind.Stall] = new Color(188, 130, 86, 255)
    };

    private readonly Font _font;

    public WorldRenderer(Font font)
    {
        _font = font;
    }

    public void Draw(SimulationHost host, Rectangle viewport)
    {
        var world = host.Simulation.World;
        Raylib.BeginScissorMode((int)viewport.X, (int)viewport.Y, (int)viewport.Width, (int)viewport.Height);
        Raylib.BeginMode2D(host.Camera);

        DrawTiles(world, host);
        if (host.ShowGrid)
        {
            DrawGrid(world);
        }

        if (world.Game.AnyEnabled)
        {
            DrawBeds(world);
        }
        DrawGroundItems(host);
        if (host.ShowTrust && host.SelectedNpc is not null)
        {
            DrawTrustLines(host);
        }
        if (host.ShowHeatmap && host.SelectedEventId is int eventId)
        {
            DrawHeatmap(host, eventId);
        }
        if (host.SelectedNpc is not null)
        {
            if (host.ShowVision)
            {
                DrawRadius(host.SelectedNpc, host.VisionRadiusOf(host.SelectedNpc), new Color(255, 255, 255, 60));
            }
            if (host.ShowHearing)
            {
                DrawRadius(host.SelectedNpc, world.Config.HearingRadius, new Color(120, 200, 255, 60));
            }
        }

        DrawNpcs(host);
        DrawPlayer(host);
        DrawEventMarker(host);

        Raylib.EndMode2D();
        Raylib.EndScissorMode();
    }

    private void DrawTiles(World world, SimulationHost host)
    {
        var map = world.Map;
        var topLeft = Raylib.GetScreenToWorld2D(new Vector2(0, 36), host.Camera);
        var bottomRight = Raylib.GetScreenToWorld2D(
            new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), host.Camera);

        var fromX = Math.Max(0, (int)(topLeft.X / 24) - 1);
        var toX = Math.Min(map.Width - 1, (int)(bottomRight.X / 24) + 1);
        var fromY = Math.Max(0, (int)(topLeft.Y / 24) - 1);
        var toY = Math.Min(map.Height - 1, (int)(bottomRight.Y / 24) + 1);

        for (var y = fromY; y <= toY; y++)
        {
            for (var x = fromX; x <= toX; x++)
            {
                var kind = map.TileAt(x, y);
                var color = TileColors.TryGetValue(kind, out var tileColor) ? tileColor : Color.Gray;
                Raylib.DrawRectangle(x * 24, y * 24, 24, 24, color);
                DecorateTile(x, y, kind);
            }
        }

        DrawZoneLabels(world, host);

        if (world.Clock.IsNight(world.Config))
        {
            Raylib.DrawRectangle(0, 0, map.Width * 24, map.Height * 24, new Color(10, 20, 60, 70));
        }
    }

    /// <summary>Мелкие детали тайлов: доски пола, верхняя кромка стены, крона, рябь, полог прилавка.</summary>
    private static void DecorateTile(int x, int y, TileKind kind)
    {
        var px = x * 24;
        var py = y * 24;

        switch (kind)
        {
            case TileKind.Floor:
            {
                var line = new Color(0, 0, 0, 26);
                Raylib.DrawLine(px, py + 8, px + 24, py + 8, line);
                Raylib.DrawLine(px, py + 16, px + 24, py + 16, line);
                break;
            }
            case TileKind.Wall:
            {
                Raylib.DrawRectangle(px, py, 24, 3, new Color(104, 104, 120, 255));
                Raylib.DrawRectangle(px, py + 21, 24, 3, new Color(44, 44, 54, 255));
                break;
            }
            case TileKind.Tree:
            {
                Raylib.DrawRectangle(px + 10, py + 16, 4, 7, new Color(66, 48, 30, 255));
                Raylib.DrawCircle(px + 12, py + 11, 9, new Color(38, 78, 44, 255));
                Raylib.DrawCircle(px + 9, py + 8, 5, new Color(64, 112, 62, 255));
                break;
            }
            case TileKind.Water:
            {
                Raylib.DrawLine(px + 3, py + 8, px + 9, py + 8, new Color(150, 200, 240, 90));
                Raylib.DrawLine(px + 13, py + 14, px + 20, py + 14, new Color(150, 200, 240, 70));
                break;
            }
            case TileKind.Stall:
            {
                Raylib.DrawRectangle(px + 2, py + 2, 20, 6, new Color(214, 150, 96, 255));
                Raylib.DrawRectangle(px + 2, py + 8, 9, 4, new Color(240, 236, 226, 220));
                Raylib.DrawRectangle(px + 13, py + 8, 9, 4, new Color(240, 236, 226, 220));
                break;
            }
            case TileKind.Window:
            {
                Raylib.DrawLine(px + 12, py + 2, px + 12, py + 22, new Color(255, 255, 255, 90));
                Raylib.DrawLine(px + 2, py + 12, px + 22, py + 12, new Color(255, 255, 255, 90));
                break;
            }
            case TileKind.DoorOpen:
            case TileKind.DoorClosed:
            {
                Raylib.DrawRectangle(px + 4, py + 4, 16, 16, new Color(0, 0, 0, 40));
                break;
            }
        }
    }

    private void DrawZoneLabels(World world, SimulationHost host)
    {
        if (host.Camera.Zoom < 0.85f)
        {
            return;
        }

        foreach (var zone in world.Map.Zones)
        {
            var center = new Vector2(zone.CenterX * 24 + 12, zone.CenterY * 24 + 12);
            var size = Raylib.MeasureTextEx(_font, zone.NameUk, 12, 0.5f);
            var position = new Vector2(center.X - size.X / 2, center.Y - size.Y / 2);
            Raylib.DrawRectangle((int)(position.X - 5), (int)(position.Y - 2), (int)size.X + 10, (int)size.Y + 4,
                new Color(0, 0, 0, 110));
            Raylib.DrawTextEx(_font, zone.NameUk, position, 12, 0.5f, new Color(255, 244, 214, 210));
        }
    }

    private static void DrawGrid(World world)
    {
        var color = new Color(0, 0, 0, 40);
        for (var x = 0; x <= world.Map.Width; x++)
        {
            Raylib.DrawLine(x * 24, 0, x * 24, world.Map.Height * 24, color);
        }
        for (var y = 0; y <= world.Map.Height; y++)
        {
            Raylib.DrawLine(0, y * 24, world.Map.Width * 24, y * 24, color);
        }
    }

    /// <summary>Кровати жителей и игрока: рама, подушка, одеяло.</summary>
    private static void DrawBeds(World world)
    {
        foreach (var bed in world.Game.Beds.Values)
        {
            DrawBed(bed.X, bed.Y, new Color(150, 64, 56, 255));
        }
        if (world.Game.PlayerBed is { } own)
        {
            DrawBed(own.X, own.Y, new Color(64, 104, 168, 255));
        }
    }

    private static void DrawBed(int tileX, int tileY, Color blanket)
    {
        var x = tileX * 24;
        var y = tileY * 24;
        Raylib.DrawRectangle(x + 3, y + 2, 18, 20, new Color(104, 70, 40, 255));
        Raylib.DrawRectangle(x + 5, y + 4, 14, 5, new Color(236, 232, 220, 255));
        Raylib.DrawRectangle(x + 5, y + 10, 14, 10, blanket);
        Raylib.DrawRectangleLines(x + 3, y + 2, 18, 20, new Color(40, 28, 18, 255));
    }

    private static void DrawGroundItems(SimulationHost host)
    {
        foreach (var ground in host.Simulation.World.GroundItems)
        {
            DrawItemIcon(ground.ItemId, ground.X * 24 + 12, ground.Y * 24 + 12);
        }
    }

    /// <summary>Простые нарисованные иконки предметов (без файлов спрайтов).</summary>
    public static void DrawItemIcon(string itemId, float cx, float cy)
    {
        var outline = new Color(20, 20, 24, 255);
        switch (itemId)
        {
            case "silver_cup":
            {
                var silver = new Color(214, 218, 226, 255);
                Raylib.DrawTriangle(new Vector2(cx - 6, cy - 7), new Vector2(cx, cy + 1), new Vector2(cx + 6, cy - 7), silver);
                Raylib.DrawRectangle((int)cx - 6, (int)cy - 8, 12, 2, silver);
                Raylib.DrawRectangle((int)cx - 1, (int)cy, 2, 5, silver);
                Raylib.DrawRectangle((int)cx - 4, (int)cy + 5, 8, 2, silver);
                Raylib.DrawLineEx(new Vector2(cx - 6, cy - 8), new Vector2(cx + 6, cy - 8), 1, outline);
                break;
            }
            case "chest":
            {
                Raylib.DrawRectangle((int)cx - 8, (int)cy - 5, 16, 11, new Color(126, 82, 44, 255));
                Raylib.DrawRectangle((int)cx - 8, (int)cy - 5, 16, 4, new Color(98, 62, 32, 255));
                Raylib.DrawRectangle((int)cx - 2, (int)cy - 2, 4, 4, new Color(232, 196, 70, 255));
                Raylib.DrawRectangleLines((int)cx - 8, (int)cy - 5, 16, 11, outline);
                break;
            }
            case "bread":
            {
                Raylib.DrawEllipse((int)cx, (int)cy, 8, 5, new Color(204, 150, 78, 255));
                Raylib.DrawLine((int)cx - 4, (int)cy - 3, (int)cx - 2, (int)cy + 3, new Color(150, 100, 50, 255));
                Raylib.DrawLine((int)cx + 1, (int)cy - 3, (int)cx + 3, (int)cy + 3, new Color(150, 100, 50, 255));
                break;
            }
            case "fish":
            {
                var body = new Color(120, 150, 176, 255);
                Raylib.DrawEllipse((int)cx - 1, (int)cy, 7, 4, body);
                Raylib.DrawTriangle(new Vector2(cx + 5, cy), new Vector2(cx + 10, cy + 4), new Vector2(cx + 10, cy - 4), body);
                Raylib.DrawCircle((int)cx - 5, (int)cy - 1, 1, outline);
                break;
            }
            case "pie":
            {
                Raylib.DrawCircle((int)cx, (int)cy, 7, new Color(214, 160, 82, 255));
                Raylib.DrawCircleLines((int)cx, (int)cy, 7, new Color(150, 96, 40, 255));
                Raylib.DrawLine((int)cx - 4, (int)cy, (int)cx + 4, (int)cy, new Color(150, 96, 40, 255));
                Raylib.DrawLine((int)cx, (int)cy - 4, (int)cx, (int)cy + 4, new Color(150, 96, 40, 255));
                break;
            }
            case "apple":
            {
                Raylib.DrawCircle((int)cx, (int)cy + 1, 6, new Color(196, 44, 44, 255));
                Raylib.DrawLine((int)cx, (int)cy - 5, (int)cx + 1, (int)cy - 8, new Color(90, 60, 30, 255));
                Raylib.DrawEllipse((int)cx + 3, (int)cy - 7, 3, 1.5f, new Color(70, 150, 60, 255));
                break;
            }
            case "wine":
            {
                Raylib.DrawEllipse((int)cx, (int)cy + 2, 6, 6, new Color(120, 40, 90, 255));
                Raylib.DrawRectangle((int)cx - 2, (int)cy - 8, 4, 6, new Color(120, 40, 90, 255));
                Raylib.DrawRectangleLines((int)cx - 2, (int)cy - 8, 4, 6, outline);
                break;
            }
            case "ring":
            {
                Raylib.DrawCircleLines((int)cx, (int)cy + 2, 5, new Color(220, 220, 230, 255));
                Raylib.DrawCircleLines((int)cx, (int)cy + 2, 4, new Color(220, 220, 230, 255));
                Raylib.DrawCircle((int)cx, (int)cy - 4, 2, new Color(90, 180, 230, 255));
                break;
            }
            case "letter":
            {
                Raylib.DrawRectangle((int)cx - 7, (int)cy - 5, 14, 10, new Color(236, 230, 210, 255));
                Raylib.DrawLine((int)cx - 7, (int)cy - 5, (int)cx, (int)cy + 1, outline);
                Raylib.DrawLine((int)cx + 7, (int)cy - 5, (int)cx, (int)cy + 1, outline);
                Raylib.DrawRectangleLines((int)cx - 7, (int)cy - 5, 14, 10, outline);
                break;
            }
            case "tools":
            case "sword":
            {
                var metal = new Color(170, 174, 186, 255);
                Raylib.DrawLineEx(new Vector2(cx - 7, cy + 7), new Vector2(cx + 6, cy - 6), 2.5f, metal);
                Raylib.DrawLineEx(new Vector2(cx - 4, cy + 1), new Vector2(cx + 1, cy + 6), 2, new Color(110, 72, 40, 255));
                if (itemId == "tools")
                {
                    Raylib.DrawLineEx(new Vector2(cx + 7, cy + 7), new Vector2(cx - 6, cy - 6), 2.5f, metal);
                }
                break;
            }
            case "grain_sack":
            {
                Raylib.DrawRectangleRounded(new Rectangle(cx - 6, cy - 5, 12, 12), 0.5f, 6, new Color(212, 190, 140, 255));
                Raylib.DrawRectangle((int)cx - 3, (int)cy - 7, 6, 3, new Color(170, 140, 90, 255));
                break;
            }
            case "warehouse_key":
            {
                var gold = new Color(226, 192, 70, 255);
                Raylib.DrawCircleLines((int)cx - 4, (int)cy, 3, gold);
                Raylib.DrawLineEx(new Vector2(cx - 1, cy), new Vector2(cx + 7, cy), 2, gold);
                Raylib.DrawLineEx(new Vector2(cx + 5, cy), new Vector2(cx + 5, cy + 3), 2, gold);
                break;
            }
            case "red_scarf":
            {
                Raylib.DrawTriangle(new Vector2(cx - 7, cy - 5), new Vector2(cx, cy + 7), new Vector2(cx + 7, cy - 5), new Color(196, 40, 40, 255));
                break;
            }
            default:
            {
                Raylib.DrawRectangle((int)cx - 4, (int)cy - 4, 8, 8, new Color(200, 170, 120, 255));
                Raylib.DrawRectangleLines((int)cx - 4, (int)cy - 4, 8, 8, outline);
                break;
            }
        }
    }

    private void DrawNpcs(SimulationHost host)
    {
        var world = host.Simulation.World;
        var zoom = host.Camera.Zoom;
        var occupied = new List<Rectangle>();

        // выбранный и наведённый рисуются последними: их подписи должны побеждать в коллизиях
        var ordered = world.ActiveNpcs()
            .OrderBy(n => ReferenceEquals(n, host.SelectedNpc) || ReferenceEquals(n, host.HoveredNpc) ? 1 : 0)
            .ToList();

        foreach (var npc in ordered)
        {
            var center = new Vector2((float)(npc.RenderX * 24 + 12), (float)(npc.RenderY * 24 + 12));
            var (r, g, b) = Core.Entities.Roles.Color(npc.Role);
            var color = new Color(r, g, b, (byte)255);
            var knockedOut = npc.IsKnockedOut(world.Tick);
            Raylib.DrawCircleV(center, 9, knockedOut ? new Color((byte)(r / 2 + 40), (byte)(g / 2 + 40), (byte)(b / 2 + 40), (byte)255) : color);
            Raylib.DrawCircleLinesV(center, 9, Color.Black);
            if (knockedOut)
            {
                Raylib.DrawLineEx(new Vector2(center.X - 5, center.Y - 5), new Vector2(center.X + 5, center.Y + 5), 2, Color.Black);
                Raylib.DrawLineEx(new Vector2(center.X + 5, center.Y - 5), new Vector2(center.X - 5, center.Y + 5), 2, Color.Black);
            }
            if (world.Game.Combat && npc.Hp < npc.MaxHp)
            {
                DrawHealthBar(center, npc.Hp, npc.MaxHp);
            }

            if (ReferenceEquals(npc, host.SelectedNpc))
            {
                Raylib.DrawCircleLinesV(center, 13, Color.Yellow);
            }

            var status = StatusIcon(npc);
            if (status is not null && zoom >= 0.7f)
            {
                var badgeCenter = new Vector2(center.X + 10, center.Y - 14);
                Raylib.DrawCircleV(badgeCenter, 6.5f, new Color(0, 0, 0, 190));
                Raylib.DrawCircleLinesV(badgeCenter, 6.5f, new Color(255, 255, 255, 120));
                var size = 11f;
                var badgeWidth = Raylib.MeasureTextEx(_font, status, size, 0.5f).X;
                Raylib.DrawTextEx(_font, status, new Vector2(badgeCenter.X - badgeWidth / 2, badgeCenter.Y - 6),
                    size, 0.5f, Color.RayWhite);
            }

            var highlighted = ReferenceEquals(npc, host.SelectedNpc) || ReferenceEquals(npc, host.HoveredNpc);
            var showName = host.ShowNames || zoom >= 1.0f || highlighted;
            if (showName)
            {
                const float size = 13f;
                var width = Raylib.MeasureTextEx(_font, npc.Name, size, 0.5f).X;
                var label = new Rectangle(center.X - width / 2 - 2, center.Y - 32, width + 4, 16);
                if (highlighted || !occupied.Any(other => Raylib.CheckCollisionRecs(other, label)))
                {
                    occupied.Add(label);
                    Raylib.DrawRectangle((int)label.X, (int)label.Y, (int)label.Width, (int)label.Height,
                        new Color(0, 0, 0, 120));
                    Raylib.DrawTextEx(_font, npc.Name, new Vector2(label.X + 2, label.Y + 1), size, 0.5f, Color.White);
                }
            }

            if (host.HoveredNpc == npc)
            {
                Raylib.DrawCircleLinesV(center, 15, new Color(255, 255, 255, 160));
            }
        }
    }

    private static string? StatusIcon(Npc npc)
    {
        if (npc.Hp <= 0)
        {
            return "x";
        }
        if (npc.PendingDecision)
        {
            return "…";
        }
        if (npc.IsAsleep)
        {
            return "z";
        }
        if (npc.CurrentAction is not null)
        {
            return npc.CurrentAction.ActionId switch
            {
                "investigate" => "?",
                "arrest" or "shout_alarm" or "help_fire" or "attack" or "banish" or "search" => "!",
                "approach_player" => ">",
                _ => null
            };
        }
        return null;
    }

    private static void DrawPlayer(SimulationHost host)
    {
        var world = host.Simulation.World;
        var player = world.Player;
        var center = new Vector2((float)(player.RenderX * 24 + 12), (float)(player.RenderY * 24 + 12));
        var knockedOut = player.IsKnockedOut(world.Tick);
        Raylib.DrawCircleV(center, 10, knockedOut ? new Color(150, 150, 150, 255) : Color.White);
        Raylib.DrawCircleLinesV(center, 10, Color.Black);
        if ((world.Game.Combat || world.Game.Life) && player.Hp < player.MaxHp)
        {
            DrawHealthBar(center, player.Hp, player.MaxHp);
        }
        if (player.IsAsleep(world.Tick))
        {
            Raylib.DrawText("z", (int)center.X + 8, (int)center.Y - 20, 14, Color.White);
        }
        if (player.IsJailed(world.Tick))
        {
            // решётка обезьянника
            for (var i = -1; i <= 1; i++)
            {
                Raylib.DrawLineEx(new Vector2(center.X + i * 6, center.Y - 14), new Vector2(center.X + i * 6, center.Y + 14), 2,
                    new Color(60, 60, 70, 255));
            }
        }
    }

    private static void DrawHealthBar(Vector2 center, int hp, int maxHp)
    {
        const float width = 20f;
        var fraction = maxHp <= 0 ? 0 : Math.Clamp(hp / (float)maxHp, 0, 1);
        var fill = fraction > 0.6f ? new Color(80, 190, 90, 255) : fraction > 0.3f ? new Color(220, 180, 60, 255) : new Color(210, 60, 50, 255);
        Raylib.DrawRectangle((int)(center.X - width / 2), (int)(center.Y + 12), (int)width, 4, new Color(0, 0, 0, 170));
        Raylib.DrawRectangle((int)(center.X - width / 2), (int)(center.Y + 12), (int)(width * fraction), 4, fill);
    }

    private void DrawTrustLines(SimulationHost host)
    {
        var world = host.Simulation.World;
        var from = host.SelectedNpc!;
        foreach (var other in world.ActiveNpcs())
        {
            if (ReferenceEquals(other, from))
            {
                continue;
            }
            var value = world.Trust.Get(from.Id, other.Id);
            if (value < 0.35)
            {
                continue;
            }

            var start = new Vector2((float)(from.RenderX * 24 + 12), (float)(from.RenderY * 24 + 12));
            var end = new Vector2((float)(other.RenderX * 24 + 12), (float)(other.RenderY * 24 + 12));
            var alpha = (byte)Math.Clamp((value - 0.3) * 300, 20, 200);
            var color = value >= 0.6
                ? new Color((byte)80, (byte)200, (byte)120, alpha)
                : new Color((byte)200, (byte)180, (byte)80, alpha);
            Raylib.DrawLineEx(start, end, (float)(1 + value * 3), color);
        }
    }

    private static void DrawHeatmap(SimulationHost host, int eventId)
    {
        foreach (var npc in host.Simulation.World.ActiveNpcs())
        {
            var entry = npc.Knowledge.ByEventId(eventId);
            if (entry is null)
            {
                continue;
            }

            var color = entry.C >= 0.8
                ? new Color(220, 40, 40, 170)
                : entry.C >= 0.5
                    ? new Color(230, 200, 30, 170)
                    : entry.C >= 0.2
                        ? new Color(140, 140, 140, 150)
                        : new Color(90, 90, 90, 90);
            var center = new Vector2((float)(npc.RenderX * 24 + 12), (float)(npc.RenderY * 24 + 12));
            Raylib.DrawRing(center, 10f, 15f, 0f, 360f, 24, color);
        }
    }

    private static void DrawRadius(Npc npc, double radiusTiles, Color color)
    {
        var center = new Vector2((float)(npc.RenderX * 24 + 12), (float)(npc.RenderY * 24 + 12));
        Raylib.DrawCircleLinesV(center, (float)(radiusTiles * 24), color);
    }

    private static void DrawEventMarker(SimulationHost host)
    {
        if (host.SelectedEventId is not int eventId)
        {
            return;
        }
        var worldEvent = host.Simulation.World.Log.ById(eventId);
        if (worldEvent is null)
        {
            return;
        }

        var pulse = (float)(0.5 + 0.5 * Math.Sin(Raylib.GetTime() * 6));
        var center = new Vector2(worldEvent.X * 24 + 12, worldEvent.Y * 24 + 12);
        var color = new Color((byte)255, (byte)(120 + 100 * pulse), (byte)0, (byte)200);
        Raylib.DrawCircleLinesV(center, 10 + 6 * pulse, color);
        Raylib.DrawCircleV(center, 3, color);
    }
}
