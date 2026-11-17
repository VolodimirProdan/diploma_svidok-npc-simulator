using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Svidok.Core.Util;

namespace Svidok.Core.Config;

public sealed class AppConfig
{
    public LlmConfig Llm { get; set; } = new();

    public SimulationConfig Simulation { get; set; } = new();

    public UiConfig Ui { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static AppConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json, Options);
        if (config is null)
        {
            throw new InvalidOperationException("Не вдалося прочитати конфігурацію: " + path);
        }
        return config;
    }

    public static AppConfig LoadDefault()
    {
        var path = PathResolver.ResolveExisting("appsettings.json");
        if (!File.Exists(path))
        {
            return new AppConfig();
        }
        return Load(path);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);
}

public sealed class LlmConfig
{
    public string Provider { get; set; } = "deepseek";

    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string ApiKeyEnv { get; set; } = "DEEPSEEK_API_KEY";

    public string Model { get; set; } = "deepseek-flash";

    public double Temperature { get; set; } = 0.3;

    public int MaxTokens { get; set; } = 1200;

    public int TimeoutSec { get; set; } = 90;

    public int MaxConcurrent { get; set; } = 8;

    public string Mode { get; set; } = "LiveWithCache";

    public int MaxPromptTokensV1 { get; set; } = 900000;

    public double BudgetUsdPerRun { get; set; } = 1.0;

    public PriceTable Prices { get; set; } = new();

    public LlmMode ResolveMode()
    {
        return Mode?.Trim().ToLowerInvariant() switch
        {
            "live" => LlmMode.Live,
            "replayonly" => LlmMode.ReplayOnly,
            "mock" => LlmMode.Mock,
            _ => LlmMode.LiveWithCache
        };
    }

    public LlmConfig Clone()
    {
        return new LlmConfig
        {
            Provider = Provider,
            BaseUrl = BaseUrl,
            ApiKeyEnv = ApiKeyEnv,
            Model = Model,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            TimeoutSec = TimeoutSec,
            MaxConcurrent = MaxConcurrent,
            Mode = Mode,
            MaxPromptTokensV1 = MaxPromptTokensV1,
            BudgetUsdPerRun = BudgetUsdPerRun,
            Prices = new PriceTable
            {
                OffPeak = new PriceSet { InputHit = Prices.OffPeak.InputHit, InputMiss = Prices.OffPeak.InputMiss, Output = Prices.OffPeak.Output },
                Peak = new PriceSet { InputHit = Prices.Peak.InputHit, InputMiss = Prices.Peak.InputMiss, Output = Prices.Peak.Output },
                PeakHoursUtc = (string[])Prices.PeakHoursUtc.Clone()
            }
        };
    }
}

public enum LlmMode
{
    Live,
    LiveWithCache,
    ReplayOnly,
    Mock
}

public sealed class PriceTable
{
    public PriceSet OffPeak { get; set; } = new() { InputHit = 0.003, InputMiss = 0.15, Output = 0.60 };

    public PriceSet Peak { get; set; } = new() { InputHit = 0.006, InputMiss = 0.30, Output = 1.20 };

    public string[] PeakHoursUtc { get; set; } = ["01:00-04:00", "06:00-10:00"];

    /// <summary>Пик: 01:00–04:00 и 06:00–10:00 UTC по будням.</summary>
    public bool IsPeak(DateTime utcNow)
    {
        if (utcNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }
        var minute = utcNow.Hour * 60 + utcNow.Minute;
        foreach (var range in PeakHoursUtc)
        {
            var parts = range.Split('-');
            if (parts.Length != 2)
            {
                continue;
            }
            if (!TryParseHhMm(parts[0], out var from) || !TryParseHhMm(parts[1], out var to))
            {
                continue;
            }
            if (minute >= from && minute < to)
            {
                return true;
            }
        }
        return false;
    }

    public PriceSet For(DateTime utcNow) => IsPeak(utcNow) ? Peak : OffPeak;

    private static bool TryParseHhMm(string text, out int minutes)
    {
        minutes = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
        {
            return false;
        }
        minutes = h * 60 + m;
        return true;
    }
}

public sealed class PriceSet
{
    public double InputHit { get; set; }

    public double InputMiss { get; set; }

    public double Output { get; set; }
}

public sealed class TrustDefaults
{
    public double Family { get; set; } = 0.9;

    public double Friend { get; set; } = 0.75;

    public double Colleague { get; set; } = 0.6;

    public double Neighbour { get; set; } = 0.5;

    public double Default { get; set; } = 0.45;

    public double Stranger { get; set; } = 0.3;

    public double Player { get; set; } = 0.3;

    public double HeadmanBonus { get; set; } = 0.1;
}

public sealed class SimulationConfig
{
    public double VisionRadius { get; set; } = 10;

    public double HearingRadius { get; set; } = 6;

    public double NightVisionFactor { get; set; } = 0.5;

    public int NightFromMinute { get; set; } = 1320;

    public int NightToMinute { get; set; } = 300;

    public double HeardCredibility { get; set; } = 0.8;

    public double OverheardFactor { get; set; } = 0.8;

    public double CForget { get; set; } = 0.05;

    public double LambdaRumorPerHour { get; set; } = 0.02;

    public double LambdaFirstHandPerHour { get; set; } = 0.005;

    public int MemoryCapacity { get; set; } = 200;

    public double TalkDistance { get; set; } = 2;

    public double PTalkPerTick { get; set; } = 0.15;

    public int TalkDurationTicks { get; set; } = 5;

    public int TalkCooldownTicks { get; set; } = 60;

    public int MaxSharedPerTalk { get; set; } = 2;

    public double PDistortBase { get; set; } = 0.1;

    public double PDistortByDoubt { get; set; } = 0.2;

    public TrustDefaults TrustDefaults { get; set; } = new();

    public double TrustUpConfirm { get; set; } = 0.05;

    public double TrustDownContradict { get; set; } = 0.15;

    public double TriggerImportance { get; set; } = 0.5;

    public double TriggerMinC { get; set; } = 0.2;

    public int TriggerCooldownTicks { get; set; } = 30;

    public int MaxKnowledgeInPrompt { get; set; } = 60;

    public double FillerPerNpcPerHour { get; set; } = 6;

    public bool WaitForLlm { get; set; }

    public bool RumorsEnabled { get; set; } = true;

    public double PlayerSpeed { get; set; } = 4;

    public double TrustMultiplier { get; set; } = 1.0;

    public double TalkativenessMultiplier { get; set; } = 1.0;

    public SimulationConfig Clone()
    {
        return new SimulationConfig
        {
            VisionRadius = VisionRadius,
            HearingRadius = HearingRadius,
            NightVisionFactor = NightVisionFactor,
            NightFromMinute = NightFromMinute,
            NightToMinute = NightToMinute,
            HeardCredibility = HeardCredibility,
            OverheardFactor = OverheardFactor,
            CForget = CForget,
            LambdaRumorPerHour = LambdaRumorPerHour,
            LambdaFirstHandPerHour = LambdaFirstHandPerHour,
            MemoryCapacity = MemoryCapacity,
            TalkDistance = TalkDistance,
            PTalkPerTick = PTalkPerTick,
            TalkDurationTicks = TalkDurationTicks,
            TalkCooldownTicks = TalkCooldownTicks,
            MaxSharedPerTalk = MaxSharedPerTalk,
            PDistortBase = PDistortBase,
            PDistortByDoubt = PDistortByDoubt,
            TrustDefaults = new TrustDefaults
            {
                Family = TrustDefaults.Family,
                Friend = TrustDefaults.Friend,
                Colleague = TrustDefaults.Colleague,
                Neighbour = TrustDefaults.Neighbour,
                Default = TrustDefaults.Default,
                Stranger = TrustDefaults.Stranger,
                Player = TrustDefaults.Player,
                HeadmanBonus = TrustDefaults.HeadmanBonus
            },
            TrustUpConfirm = TrustUpConfirm,
            TrustDownContradict = TrustDownContradict,
            TriggerImportance = TriggerImportance,
            TriggerMinC = TriggerMinC,
            TriggerCooldownTicks = TriggerCooldownTicks,
            MaxKnowledgeInPrompt = MaxKnowledgeInPrompt,
            FillerPerNpcPerHour = FillerPerNpcPerHour,
            WaitForLlm = WaitForLlm,
            RumorsEnabled = RumorsEnabled,
            PlayerSpeed = PlayerSpeed,
            TrustMultiplier = TrustMultiplier,
            TalkativenessMultiplier = TalkativenessMultiplier
        };
    }

    /// <summary>Переопределения из сценария: любой ключ блока overrides.</summary>
    public void ApplyOverrides(JsonElement overrides)
    {
        if (overrides.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = typeof(SimulationConfig);
        foreach (var property in overrides.EnumerateObject())
        {
            var target = type.GetProperty(property.Name, System.Reflection.BindingFlags.Public |
                                                          System.Reflection.BindingFlags.Instance |
                                                          System.Reflection.BindingFlags.IgnoreCase);
            if (target is null || !target.CanWrite)
            {
                Log.Warn("Невідомий параметр overrides: " + property.Name);
                continue;
            }

            try
            {
                if (target.PropertyType == typeof(TrustDefaults))
                {
                    var nested = property.Value.Deserialize<TrustDefaults>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (nested is not null)
                    {
                        target.SetValue(this, nested);
                    }
                    continue;
                }

                var value = property.Value.Deserialize(target.PropertyType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (value is not null)
                {
                    target.SetValue(this, value);
                }
            }
            catch (JsonException ex)
            {
                Log.Warn("Не вдалося застосувати overrides." + property.Name + ": " + ex.Message);
            }
        }
    }
}

public sealed class UiConfig
{
    public int Width { get; set; } = 1600;

    public int Height { get; set; } = 900;

    public int TilePx { get; set; } = 24;

    public string Font { get; set; } = "assets/fonts/NotoSans-Regular.ttf";

    public int UiFontPx { get; set; } = 18;

    public int MapLabelPx { get; set; } = 13;

    public bool ResearcherMode { get; set; } = true;
}
