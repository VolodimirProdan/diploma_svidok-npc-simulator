using Svidok.Core.WorldModel;

namespace Svidok.Core.Perception;

/// <summary>Линия видимости по Брезенхэму (раздел 7.2).</summary>
public static class LineOfSight
{
    public static List<(int X, int Y)> Line((int X, int Y) from, (int X, int Y) to)
    {
        var points = new List<(int X, int Y)>();
        var x0 = from.X;
        var y0 = from.Y;
        var x1 = to.X;
        var y1 = to.Y;

        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;

        while (true)
        {
            points.Add((x0, y0));
            if (x0 == x1 && y0 == y1)
            {
                break;
            }
            var e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return points;
    }

    /// <summary>Линия закрыта, если хотя бы один промежуточный тайл закрывает обзор.</summary>
    public static bool IsClear(GameMap map, (int X, int Y) from, (int X, int Y) to)
    {
        var line = Line(from, to);
        for (var i = 1; i < line.Count - 1; i++)
        {
            if (map.BlocksSight(line[i].X, line[i].Y))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Число тайлов стен, закрытых дверей и окон на линии (для слуха).</summary>
    public static int CountBlockers(GameMap map, (int X, int Y) from, (int X, int Y) to)
    {
        var line = Line(from, to);
        var count = 0;
        for (var i = 1; i < line.Count - 1; i++)
        {
            if (TileKinds.BlocksSound(map.TileAt(line[i].X, line[i].Y)))
            {
                count++;
            }
        }
        return count;
    }
}
