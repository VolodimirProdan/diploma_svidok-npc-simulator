namespace Svidok.Core.WorldModel;

public enum TileKind
{
    Grass,
    Road,
    Square,
    Floor,
    Wall,
    DoorOpen,
    DoorClosed,
    Window,
    Tree,
    Water,
    Stall
}

public static class TileKinds
{
    public static TileKind FromChar(char c)
    {
        return c switch
        {
            '.' => TileKind.Grass,
            '=' => TileKind.Road,
            ':' => TileKind.Square,
            '_' => TileKind.Floor,
            '#' => TileKind.Wall,
            'D' => TileKind.DoorOpen,
            'd' => TileKind.DoorClosed,
            'W' => TileKind.Window,
            'T' => TileKind.Tree,
            '~' => TileKind.Water,
            'S' => TileKind.Stall,
            _ => TileKind.Grass
        };
    }

    public static char ToChar(TileKind kind)
    {
        return kind switch
        {
            TileKind.Grass => '.',
            TileKind.Road => '=',
            TileKind.Square => ':',
            TileKind.Floor => '_',
            TileKind.Wall => '#',
            TileKind.DoorOpen => 'D',
            TileKind.DoorClosed => 'd',
            TileKind.Window => 'W',
            TileKind.Tree => 'T',
            TileKind.Water => '~',
            TileKind.Stall => 'S',
            _ => '.'
        };
    }

    /// <summary>Закрытая дверь проходима только владельцу ключа; для навигации считаем её проходимой, но дорогой.</summary>
    public static bool IsWalkable(TileKind kind)
    {
        return kind switch
        {
            TileKind.Grass => true,
            TileKind.Road => true,
            TileKind.Square => true,
            TileKind.Floor => true,
            TileKind.DoorOpen => true,
            TileKind.DoorClosed => true,
            _ => false
        };
    }

    public static bool BlocksSight(TileKind kind)
    {
        return kind switch
        {
            TileKind.Wall => true,
            TileKind.Tree => true,
            TileKind.DoorClosed => true,
            _ => false
        };
    }

    public static bool BlocksSound(TileKind kind)
    {
        return kind switch
        {
            TileKind.Wall => true,
            TileKind.DoorClosed => true,
            TileKind.Window => true,
            _ => false
        };
    }

    /// <summary>Множитель звука за тайл: стены, окна и закрытые двери дают 0.5.</summary>
    public static double SoundFactor(TileKind kind) => BlocksSound(kind) ? 0.5 : 1.0;

    public static double PathCost(TileKind kind)
    {
        return kind switch
        {
            TileKind.Road => 0.8,
            TileKind.DoorClosed => 1.5,
            _ => 1.0
        };
    }
}
