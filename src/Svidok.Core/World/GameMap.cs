namespace Svidok.Core.WorldModel;

/// <summary>Карта мира: сетка тайлов 48 × 32, зоны, принадлежность тайлов зонам.</summary>
public sealed class GameMap
{
    private readonly TileKind[,] _tiles;
    private readonly Zone?[,] _zoneOf;
    private readonly Dictionary<string, List<(int X, int Y)>> _walkableByZone = new(StringComparer.Ordinal);

    public GameMap(int width, int height, TileKind[,] tiles, IReadOnlyList<Zone> zones)
    {
        Width = width;
        Height = height;
        _tiles = tiles;
        Zones = zones;
        _zoneOf = new Zone?[width, height];
        foreach (var zone in zones)
        {
            for (var y = zone.Y0; y <= zone.Y1; y++)
            {
                for (var x = zone.X0; x <= zone.X1; x++)
                {
                    if (x < 0 || y < 0 || x >= width || y >= height)
                    {
                        continue;
                    }
                    _zoneOf[x, y] ??= zone;
                }
            }
        }

        foreach (var zone in zones)
        {
            var list = new List<(int X, int Y)>();
            for (var y = zone.Y0; y <= zone.Y1; y++)
            {
                for (var x = zone.X0; x <= zone.X1; x++)
                {
                    if (InBounds(x, y) && IsWalkable(x, y))
                    {
                        list.Add((x, y));
                    }
                }
            }
            _walkableByZone[zone.Id] = list;
        }
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<Zone> Zones { get; }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public TileKind TileAt(int x, int y) => InBounds(x, y) ? _tiles[x, y] : TileKind.Wall;

    /// <summary>Центр тайла в пикселях мира (тайл 24 px по умолчанию).</summary>
    public static double TileCenter(int tile) => tile * 24.0 + 12.0;

    public bool IsWalkable(int x, int y) => InBounds(x, y) && TileKinds.IsWalkable(_tiles[x, y]);

    public bool BlocksSight(int x, int y) => !InBounds(x, y) || TileKinds.BlocksSight(_tiles[x, y]);

    public Zone? ZoneAt(int x, int y) => InBounds(x, y) ? _zoneOf[x, y] : null;

    public Zone? ZoneById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        foreach (var zone in Zones)
        {
            if (string.Equals(zone.Id, id, StringComparison.Ordinal))
            {
                return zone;
            }
        }
        return null;
    }

    public IReadOnlyList<(int X, int Y)> WalkableTilesIn(string? zoneId)
    {
        if (zoneId is not null && _walkableByZone.TryGetValue(zoneId, out var cached))
        {
            return cached;
        }
        return Array.Empty<(int X, int Y)>();
    }

    public bool ZoneHasWalkableTile(string? zoneId) => WalkableTilesIn(zoneId).Count > 0;

    /// <summary>Случайный проходимый тайл зоны (детерминированно через переданный Random).</summary>
    public (int X, int Y) RandomTileIn(string? zoneId, Random random, (int X, int Y)? fallback = null)
    {
        var tiles = WalkableTilesIn(zoneId);
        if (tiles.Count > 0)
        {
            return tiles[random.Next(tiles.Count)];
        }
        if (fallback.HasValue && IsWalkable(fallback.Value.X, fallback.Value.Y))
        {
            return fallback.Value;
        }
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (IsWalkable(x, y))
                {
                    return (x, y);
                }
            }
        }
        return (0, 0);
    }

    public static GameMap FromAscii(IReadOnlyList<string> rows, IReadOnlyList<Zone>? zones = null)
    {
        if (rows.Count == 0)
        {
            throw new ArgumentException("Порожня карта", nameof(rows));
        }
        var width = rows.Max(r => r.Length);
        var height = rows.Count;
        var tiles = new TileKind[width, height];
        for (var y = 0; y < height; y++)
        {
            var row = rows[y];
            for (var x = 0; x < width; x++)
            {
                var c = x < row.Length ? row[x] : '.';
                tiles[x, y] = TileKinds.FromChar(c);
            }
        }
        return new GameMap(width, height, tiles, zones ?? DefaultZones.All);
    }

    public string[] ToAscii()
    {
        var rows = new string[Height];
        var buffer = new char[Width];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                buffer[x] = TileKinds.ToChar(_tiles[x, y]);
            }
            rows[y] = new string(buffer);
        }
        return rows;
    }

    public GameMap WithZones(IReadOnlyList<Zone> zones)
    {
        return new GameMap(Width, Height, _tiles, zones);
    }
}
