using System.Text.Json;

namespace Svidok.Core.Events;

public sealed class EventTypeDef
{
    public string Type { get; set; } = string.Empty;

    public string NameUk { get; set; } = string.Empty;

    public string PromptUk { get; set; } = string.Empty;

    /// <summary>visual | audio | both</summary>
    public string Visibility { get; set; } = "visual";

    public double Loudness { get; set; }

    public double Importance { get; set; }

    public bool Background { get; set; }

    public List<string> Fields { get; set; } = new();

    public List<string> Distortable { get; set; } = new();

    /// <summary>Множитель радиуса зрения (пожар видно дальше вдвое).</summary>
    public double VisionMultiplier { get; set; } = 1.0;

    /// <summary>Ночное уменьшение зрения не действует (пожар).</summary>
    public bool IgnoresNight { get; set; }

    public bool Visual => Visibility is "visual" or "both";

    public bool Audible => Visibility is "audio" or "both";
}

/// <summary>Схема типов событий из data/events.json.</summary>
public sealed class EventSchema
{
    private readonly Dictionary<string, EventTypeDef> _types = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, EventTypeDef> Types => _types;

    public static EventSchema Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        var schema = new EventSchema();
        JsonElement array;
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            array = doc.RootElement;
        }
        else if (doc.RootElement.TryGetProperty("types", out var types))
        {
            array = types;
        }
        else
        {
            throw new InvalidDataException("events.json має містити масив типів подій");
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var element in array.EnumerateArray())
        {
            var def = element.Deserialize<EventTypeDef>(options);
            if (def is null || string.IsNullOrWhiteSpace(def.Type))
            {
                continue;
            }
            schema._types[def.Type] = def;
        }
        return schema;
    }

    public EventTypeDef Get(string type)
    {
        if (_types.TryGetValue(type, out var def))
        {
            return def;
        }
        return new EventTypeDef
        {
            Type = type,
            NameUk = type,
            PromptUk = type,
            Visibility = "visual",
            Loudness = 0.5,
            Importance = 0.3
        };
    }

    public bool Contains(string type) => _types.ContainsKey(type);

    public double Importance(string type) => Get(type).Importance;

    public bool IsBackground(string type) => Get(type).Background;

    public static EventSchema LoadDefault() => Load(Util.PathResolver.ResolveExisting("data/events.json"));
}
