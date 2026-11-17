using System.Text.Json;
using System.Text.Json.Serialization;
using Svidok.Core.Decisions;

namespace Svidok.Llm;

/// <summary>Разбор ответа модели по правилам 9.7.</summary>
public static class ResponseParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Убирает markdown-обёртку и берёт текст от первой { до последней }.</summary>
    public static string ExtractJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }
        var text = content.Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        if (first >= 0 && last > first)
        {
            return text[first..(last + 1)];
        }
        return text;
    }

    public static bool TryParse(string? content, ActionCatalog catalog, out LlmDecision decision, out string? error)
    {
        decision = LlmDecision.Ignore();
        error = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "порожня відповідь";
            return false;
        }

        var json = ExtractJson(content);
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, Options);
        }
        catch (JsonException ex)
        {
            error = "json не розібрано: " + ex.Message;
            return false;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Action))
        {
            error = "немає поля action";
            return false;
        }

        var action = dto.Action!.Trim();
        if (!catalog.Contains(action))
        {
            error = "дія не з каталогу: " + action;
            return false;
        }

        decision = new LlmDecision(
            action,
            Trim(dto.Target),
            Trim(dto.Zone),
            Trim(dto.Item),
            ReadInt(dto.EventRef),
            Trim(dto.QuestId),
            dto.Say ?? string.Empty,
            dto.Reason ?? string.Empty,
            ReadNote(dto.Memory),
            ReadAttitude(dto.Attitude),
            ReadBool(dto.Gossip),
            ReadAmount(dto.Amount));
        return true;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const int MaxNoteLength = 200;

    /// <summary>Заметка об игроке: null, пустая строка и «null» строкой означают «нечего запоминать».</summary>
    private static string? ReadNote(JsonElement? element)
    {
        if (element is not JsonElement value || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text) || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return text.Length > MaxNoteLength ? text[..MaxNoteLength] : text;
    }

    private static int? ReadAttitude(JsonElement? element)
    {
        var number = ReadInt(element);
        if (number is null && element is JsonElement value && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var real))
        {
            number = (int)Math.Round(real);
        }
        return number is int n ? Math.Clamp(n, -2, 2) : null;
    }

    private static int? ReadAmount(JsonElement? element)
    {
        var number = ReadInt(element);
        if (number is null && element is JsonElement value && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var real))
        {
            number = (int)Math.Round(real);
        }
        return number is int n && n > 0 ? Math.Min(n, 1000) : null;
    }

    private static bool ReadBool(JsonElement? element)
    {
        if (element is not JsonElement value)
        {
            return false;
        }
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static int? ReadInt(JsonElement? element)
    {
        if (element is not JsonElement value)
        {
            return null;
        }
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt32(out var number):
                return number;
            case JsonValueKind.String when int.TryParse(value.GetString(), out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    private sealed class Dto
    {
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("target")]
        public string? Target { get; set; }

        [JsonPropertyName("zone")]
        public string? Zone { get; set; }

        [JsonPropertyName("item")]
        public string? Item { get; set; }

        [JsonPropertyName("eventRef")]
        public JsonElement? EventRef { get; set; }

        [JsonPropertyName("questId")]
        public string? QuestId { get; set; }

        [JsonPropertyName("say")]
        public string? Say { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("memory")]
        public JsonElement? Memory { get; set; }

        [JsonPropertyName("attitude")]
        public JsonElement? Attitude { get; set; }

        [JsonPropertyName("gossip")]
        public JsonElement? Gossip { get; set; }

        [JsonPropertyName("amount")]
        public JsonElement? Amount { get; set; }
    }
}
