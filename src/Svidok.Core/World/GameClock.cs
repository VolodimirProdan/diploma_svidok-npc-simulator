using Svidok.Core.Config;

namespace Svidok.Core.WorldModel;

/// <summary>Игровое время: 1 тик = 1 минута, сутки = 1440 тиков, старт в 06:00 (если не задано иначе).</summary>
public sealed class GameClock
{
    public const int TicksPerDay = 1440;
    public const int MinutesPerHour = 60;

    public GameClock(int startMinuteOfDay = 360)
    {
        StartMinuteOfDay = startMinuteOfDay;
    }

    public int StartMinuteOfDay { get; }

    public int Tick { get; private set; }

    public int AbsoluteMinute => StartMinuteOfDay + Tick;

    public int Day => AbsoluteMinute / TicksPerDay + 1;

    public int MinuteOfDay => AbsoluteMinute % TicksPerDay;

    public int Hour => MinuteOfDay / MinutesPerHour;

    public int Minute => MinuteOfDay % MinutesPerHour;

    public string TimeString => "День " + Day + ", " + Hour.ToString("00") + ":" + Minute.ToString("00");

    public string ShortTime => Hour.ToString("00") + ":" + Minute.ToString("00");

    public bool IsNight(SimulationConfig config)
    {
        var m = MinuteOfDay;
        if (config.NightFromMinute > config.NightToMinute)
        {
            return m >= config.NightFromMinute || m < config.NightToMinute;
        }
        return m >= config.NightFromMinute && m < config.NightToMinute;
    }

    /// <summary>Номер игрового часа от старта — для ежечасных событий.</summary>
    public int HourIndex => AbsoluteMinute / MinutesPerHour;

    public bool IsNewHour => MinuteOfDay % MinutesPerHour == 0;

    public void Advance() => Tick++;
}
