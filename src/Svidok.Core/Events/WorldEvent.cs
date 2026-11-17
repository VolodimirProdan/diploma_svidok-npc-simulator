namespace Svidok.Core.Events;

/// <summary>Факт, который реально произошёл в мире. Хранится в журнале и никогда не меняется.</summary>
public sealed record WorldEvent(
    int Id,
    int Tick,
    string Type,
    string? ActorId,
    string? TargetId,
    string? ItemId,
    string? ZoneId,
    int X,
    int Y,
    double Loudness,
    double Importance,
    bool IsBackground,
    IReadOnlyDictionary<string, string> Extra)
{
    public string? Get(string key) => Extra.TryGetValue(key, out var value) ? value : null;

    public string? Text => Get("text");

    public string Position(int width) => X + "," + Y;
}
