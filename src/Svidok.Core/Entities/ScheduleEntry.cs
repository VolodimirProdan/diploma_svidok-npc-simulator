namespace Svidok.Core.Entities;

/// <summary>Запись расписания на сутки: минуты от 00:00, зона и занятие.</summary>
public sealed class ScheduleEntry
{
    public ScheduleEntry(int fromMinute, int toMinute, string zoneId, Activity activity)
    {
        FromMinute = Normalize(fromMinute);
        ToMinute = Normalize(toMinute);
        ZoneId = zoneId;
        Activity = activity;
    }

    public int FromMinute { get; }

    public int ToMinute { get; }

    public string ZoneId { get; }

    public Activity Activity { get; }

    /// <summary>Интервал может переходить через полночь (например 23:30–06:30).</summary>
    public bool Contains(int minuteOfDay)
    {
        if (FromMinute == ToMinute)
        {
            return true;
        }
        if (FromMinute < ToMinute)
        {
            return minuteOfDay >= FromMinute && minuteOfDay < ToMinute;
        }
        return minuteOfDay >= FromMinute || minuteOfDay < ToMinute;
    }

    public int DurationMinutes => FromMinute < ToMinute
        ? ToMinute - FromMinute
        : 1440 - FromMinute + ToMinute;

    /// <summary>Первый тик интервала после указанного абсолютного тика суток (0..1439).</summary>
    public int NextStartFrom(int minuteOfDay)
    {
        if (Contains(minuteOfDay))
        {
            return 0;
        }
        if (FromMinute > minuteOfDay)
        {
            return FromMinute - minuteOfDay;
        }
        return 1440 - minuteOfDay + FromMinute;
    }

    public static int Normalize(int minute)
    {
        var m = minute % 1440;
        return m < 0 ? m + 1440 : m;
    }
}
