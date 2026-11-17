using System.Text;
using System.Text.Json;
using Svidok.Core.Util;

namespace Svidok.Game;

/// <summary>Одна реплика разговора, сохранённая в профиле.</summary>
public sealed class SavedLine
{
    public bool Player { get; set; }

    public string Text { get; set; } = string.Empty;
}

/// <summary>Что конкретный NPC помнит об игроке в одном сценарии.</summary>
public sealed class NpcMemory
{
    /// <summary>Заметки NPC об игроке, по одной на строку: «[День 1, 10:30] обіцяв принести хліб».</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Доверие к игроку 0..1; отрицательное — ещё не задано (берётся значение по умолчанию).</summary>
    public double Trust { get; set; } = -1;

    public List<SavedLine> History { get; set; } = new();

    public int Talks { get; set; }

    /// <summary>Тик последнего разговора в текущем запуске (между запусками не переносится).</summary>
    public int LastTalkTick { get; set; } = -1;
}

/// <summary>Сохранённое состояние игрока в сценарии: где стоит и что у него есть.</summary>
public sealed class SavedPlayer
{
    public DateTime Saved { get; set; } = DateTime.Now;

    /// <summary>Игровое время на момент сохранения, только для показа.</summary>
    public string GameTime { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Coins { get; set; }

    public List<string> Inventory { get; set; } = new();

    public int Hp { get; set; } = 10;

    public double Hunger { get; set; }

    /// <summary>Сид мира, в котором сохранились: продолжение идёт в том же мире.</summary>
    public int Seed { get; set; } = 1;
}

/// <summary>Память всех NPC об игроке в одном сценарии.</summary>
public sealed class ScenarioMemory
{
    public Dictionary<string, NpcMemory> Npcs { get; set; } = new(StringComparer.Ordinal);

    public NpcMemory For(string npcId)
    {
        if (!Npcs.TryGetValue(npcId, out var memory))
        {
            memory = new NpcMemory();
            Npcs[npcId] = memory;
        }
        return memory;
    }
}

/// <summary>Профиль игрока: имя, которое видят персонажи, и их память о нём по сценариям.</summary>
public sealed class PlayerProfile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime Created { get; set; } = DateTime.Now;

    public DateTime LastPlayed { get; set; } = DateTime.Now;

    /// <summary>Последний сид, с которым играли этим профилем.</summary>
    public int LastSeed { get; set; } = 1;

    public Dictionary<string, ScenarioMemory> Scenarios { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Сохранения игрока по сценариям (id сценария).</summary>
    public Dictionary<string, SavedPlayer> Saves { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Общая память для игровых сценариев (S6, S8): там те же жители, и отношение к игроку
    /// должно переходить из одного в другой. Экспериментальные сценарии хранятся отдельно.
    /// </summary>
    public const string VillageKey = "village";

    public static string MemoryKey(Svidok.Core.Scenarios.ScenarioDefinition scenario) =>
        scenario.Features.Social ? VillageKey : scenario.Id;

    public ScenarioMemory ForScenario(Svidok.Core.Scenarios.ScenarioDefinition scenario)
    {
        var key = MemoryKey(scenario);
        if (key == VillageKey && !Scenarios.ContainsKey(VillageKey) && Scenarios.TryGetValue(scenario.Id, out var old))
        {
            // память, накопленная до общей деревни, не теряется
            Scenarios[VillageKey] = old;
            Scenarios.Remove(scenario.Id);
        }
        return ForScenario(key);
    }

    public ScenarioMemory ForScenario(string scenarioId)
    {
        if (!Scenarios.TryGetValue(scenarioId, out var memory))
        {
            memory = new ScenarioMemory();
            Scenarios[scenarioId] = memory;
        }
        return memory;
    }
}

/// <summary>Профили в папке profiles/ рядом с решением: один json-файл на профиль.</summary>
public static class PlayerProfiles
{
    public const int MaxNameLength = 32;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string Directory => PathResolver.EnsureDirectory("profiles");

    public static List<PlayerProfile> LoadAll()
    {
        var result = new List<PlayerProfile>();
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<PlayerProfile>(File.ReadAllText(file, Encoding.UTF8), Options);
                if (profile is null || string.IsNullOrWhiteSpace(profile.Name))
                {
                    continue;
                }
                profile.Id = Path.GetFileNameWithoutExtension(file);
                result.Add(profile);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warn("Профіль не прочитано: " + file + " — " + ex.Message);
            }
        }
        return result.OrderByDescending(p => p.LastPlayed).ToList();
    }

    public static PlayerProfile Create(string name)
    {
        var clean = CleanName(name);
        var profile = new PlayerProfile
        {
            Id = UniqueId(clean),
            Name = clean,
            Created = DateTime.Now,
            LastPlayed = DateTime.Now
        };
        Save(profile);
        return profile;
    }

    public static void Rename(PlayerProfile profile, string name)
    {
        profile.Name = CleanName(name);
        Save(profile);
    }

    public static void Delete(PlayerProfile profile)
    {
        var path = PathOf(profile.Id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Забыть всё, что персонажи помнят об игроке (имя остаётся).</summary>
    public static void ResetMemory(PlayerProfile profile)
    {
        profile.Scenarios.Clear();
        Save(profile);
    }

    public static void Save(PlayerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = UniqueId(profile.Name);
        }
        var path = PathOf(profile.Id);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(profile, Options), Encoding.UTF8);
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Профіль не збережено: " + path + " — " + ex.Message);
        }
    }

    public static string CleanName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            trimmed = "Мандрівник";
        }
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength] : trimmed;
    }

    private static string PathOf(string id) => Path.Combine(Directory, id + ".json");

    private static string UniqueId(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (sb.Length > 0 && sb[^1] != '_')
            {
                sb.Append('_');
            }
        }
        var baseId = sb.ToString().Trim('_');
        if (baseId.Length == 0)
        {
            baseId = "profile";
        }
        var id = baseId;
        var n = 2;
        while (File.Exists(PathOf(id)))
        {
            id = baseId + "_" + n++;
        }
        return id;
    }
}
