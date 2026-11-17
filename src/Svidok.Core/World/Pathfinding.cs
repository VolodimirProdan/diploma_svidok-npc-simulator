namespace Svidok.Core.WorldModel;

/// <summary>A* по 4 соседям; стоимость тайла 1, дорога 0.8.</summary>
public static class Pathfinding
{
    private static readonly (int Dx, int Dy)[] Dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    public static List<(int X, int Y)>? FindPath(GameMap map, (int X, int Y) start, (int X, int Y) goal)
    {
        if (!map.InBounds(start.X, start.Y) || !map.InBounds(goal.X, goal.Y))
        {
            return null;
        }
        if (start == goal)
        {
            return new List<(int X, int Y)>();
        }
        if (!map.IsWalkable(goal.X, goal.Y))
        {
            return null;
        }

        var width = map.Width;
        var height = map.Height;
        var count = width * height;
        var gScore = new double[count];
        var cameFrom = new int[count];
        var closed = new bool[count];
        Array.Fill(gScore, double.PositiveInfinity);
        Array.Fill(cameFrom, -1);

        var startIndex = start.Y * width + start.X;
        var goalIndex = goal.Y * width + goal.X;
        gScore[startIndex] = 0;

        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(startIndex, Heuristic(start, goal));

        while (queue.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }
            closed[current] = true;
            if (current == goalIndex)
            {
                break;
            }

            var cx = current % width;
            var cy = current / width;
            foreach (var (dx, dy) in Dirs)
            {
                var nx = cx + dx;
                var ny = cy + dy;
                if (!map.InBounds(nx, ny) || !map.IsWalkable(nx, ny))
                {
                    continue;
                }
                var ni = ny * width + nx;
                if (closed[ni])
                {
                    continue;
                }
                var step = TileKinds.PathCost(map.TileAt(nx, ny));
                var tentative = gScore[current] + step;
                if (tentative < gScore[ni])
                {
                    gScore[ni] = tentative;
                    cameFrom[ni] = current;
                    var priority = tentative + Heuristic((nx, ny), goal);
                    queue.Enqueue(ni, priority);
                }
            }
        }

        if (cameFrom[goalIndex] == -1 && goalIndex != startIndex)
        {
            return null;
        }

        var path = new List<(int X, int Y)>();
        var node = goalIndex;
        while (node != startIndex && node >= 0)
        {
            path.Add((node % width, node / width));
            node = cameFrom[node];
        }
        path.Reverse();
        return path;
    }

    /// <summary>Ближайший проходимый тайл к заданному (если цель занята стеной).</summary>
    public static (int X, int Y) NearestWalkable(GameMap map, (int X, int Y) tile)
    {
        if (map.IsWalkable(tile.X, tile.Y))
        {
            return tile;
        }
        for (var radius = 1; radius < Math.Max(map.Width, map.Height); radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                    {
                        continue;
                    }
                    var x = tile.X + dx;
                    var y = tile.Y + dy;
                    if (map.IsWalkable(x, y))
                    {
                        return (x, y);
                    }
                }
            }
        }
        return tile;
    }

    public static int Manhattan((int X, int Y) a, (int X, int Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    public static double Euclidean((int X, int Y) a, (int X, int Y) b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Heuristic((int X, int Y) a, (int X, int Y) b) => Manhattan(a, b);
}
