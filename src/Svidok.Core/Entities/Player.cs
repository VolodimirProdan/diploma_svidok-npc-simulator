namespace Svidok.Core.Entities;

/// <summary>Управляемый персонаж (раздел 5.5).</summary>
public sealed class Player
{
    public const string PlayerId = "player";

    public string Id => PlayerId;

    /// <summary>Имя, под которым игрока знают персонажи (профиль игрока).</summary>
    public string Name { get; set; } = "Гравець";

    public int Hp { get; set; } = 10;

    public int MaxHp { get; set; } = 10;

    /// <summary>Голод 0..100: на 80+ здоровье понемногу падает.</summary>
    public double Hunger { get; set; }

    /// <summary>Сидит в обезьяннике до этого тика.</summary>
    public int JailedUntilTick { get; set; } = -1;

    /// <summary>Выгнан из села до этого тика.</summary>
    public int BanishedUntilTick { get; set; } = -1;

    /// <summary>Без сознания до этого тика.</summary>
    public int KnockedOutUntilTick { get; set; } = -1;

    /// <summary>Свой дом (зона карты); пустая строка — дома нет.</summary>
    public string HomeZone { get; set; } = string.Empty;

    /// <summary>Подработка: тик окончания текущей работы и где.</summary>
    public int WorkUntilTick { get; set; } = -1;

    public string? WorkZone { get; set; }

    /// <summary>Спит до этого тика (время в это время идёт быстрее).</summary>
    public int SleepUntilTick { get; set; } = -1;

    public bool IsAsleep(int tick) => SleepUntilTick > tick;

    public bool IsJailed(int tick) => JailedUntilTick > tick;

    public bool IsBanished(int tick) => BanishedUntilTick > tick;

    public bool IsKnockedOut(int tick) => KnockedOutUntilTick > tick;

    public bool IsBusy(int tick) => IsJailed(tick) || IsKnockedOut(tick) || WorkUntilTick > tick || IsAsleep(tick);

    public void Teleport(int x, int y)
    {
        X = x;
        Y = y;
        RenderX = x;
        RenderY = y;
    }

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Непрерывная позиция в тайлах для отрисовки и движения.</summary>
    public double RenderX { get; set; }

    public double RenderY { get; set; }

    public List<string> Inventory { get; } = new();

    public int Coins { get; set; }

    /// <summary>Выбранный предмет в инвентаре (для «віддати»).</summary>
    public int SelectedIndex { get; set; }

    public string? SelectedItem => Inventory.Count == 0
        ? null
        : Inventory[Math.Clamp(SelectedIndex, 0, Inventory.Count - 1)];

    public (int X, int Y) Position => (X, Y);

    public (int X, int Y) TileOf => ((int)Math.Round(RenderX), (int)Math.Round(RenderY));

    public bool HasItem(string itemId) => Inventory.Contains(itemId, StringComparer.Ordinal);

    public void AddItem(string itemId)
    {
        Inventory.Add(itemId);
        SelectedIndex = Inventory.Count - 1;
    }

    public bool RemoveItem(string itemId)
    {
        var index = Inventory.FindIndex(i => string.Equals(i, itemId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }
        Inventory.RemoveAt(index);
        SelectedIndex = Math.Clamp(SelectedIndex, 0, Math.Max(0, Inventory.Count - 1));
        return true;
    }
}
