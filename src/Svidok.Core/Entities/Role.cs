namespace Svidok.Core.Entities;

public enum Role
{
    Guard,
    Innkeeper,
    Smith,
    Trader,
    Peasant,
    Headman,
    Traveler
}

public static class Roles
{
    public static string NameUk(Role role)
    {
        return role switch
        {
            Role.Guard => "Вартовий",
            Role.Innkeeper => "Корчмар",
            Role.Smith => "Коваль",
            Role.Trader => "Торговець",
            Role.Peasant => "Селянин",
            Role.Headman => "Староста",
            Role.Traveler => "Мандрівник",
            _ => role.ToString()
        };
    }

    /// <summary>Цвет круга NPC по роли (раздел 5.4).</summary>
    public static (byte R, byte G, byte B) Color(Role role)
    {
        return role switch
        {
            Role.Guard => (52, 101, 214),
            Role.Innkeeper => (140, 90, 50),
            Role.Smith => (90, 90, 90),
            Role.Trader => (230, 190, 40),
            Role.Peasant => (70, 160, 70),
            Role.Headman => (130, 70, 170),
            Role.Traveler => (230, 120, 40),
            _ => (200, 200, 200)
        };
    }

    public static Role Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Role.Peasant;
        }
        return Enum.TryParse<Role>(value, true, out var role) ? role : Role.Peasant;
    }
}
