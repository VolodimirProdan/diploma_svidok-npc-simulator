using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Svidok.Core.Config;
using Svidok.Core.Llm;
using Svidok.Core.Util;

namespace Svidok.Llm;

/// <summary>Кэш ответов модели: ключ = SHA-256 от (model, temperature, все messages) (раздел 10.5).</summary>
public sealed class CachedLlmClient : ILlmClient
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ILlmClient _inner;
    private readonly LlmMode _mode;
    private readonly string _cacheDir;

    public CachedLlmClient(ILlmClient inner, LlmMode mode, string? cacheDir = null)
    {
        _inner = inner;
        _mode = mode;
        _cacheDir = cacheDir ?? PathResolver.EnsureDirectory("cache");
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var hash = Hash(request);
        var path = Path.Combine(_cacheDir, hash + ".json");

        if (File.Exists(path) && _mode is LlmMode.LiveWithCache or LlmMode.ReplayOnly)
        {
            var cached = Read(path, out var cachedMaxTokens);
            // ответ, упёршийся в прежний, меньший лимит токенов, обрезан: запрашиваем заново
            var truncated = cached is not null &&
                            cached.CompletionTokens >= cachedMaxTokens &&
                            request.MaxTokens > cachedMaxTokens;
            if (cached is not null && !truncated)
            {
                return cached with { FromCache = true, CacheFile = path };
            }
        }

        if (_mode == LlmMode.ReplayOnly)
        {
            var miss = LlmResult.ErrorResult("CacheMiss");
            return miss with { CacheFile = path };
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await _inner.CompleteAsync(request, ct).ConfigureAwait(false);
        stopwatch.Stop();

        if (_mode == LlmMode.LiveWithCache && result.Error is null)
        {
            Save(path, request, result, stopwatch.ElapsedMilliseconds);
        }

        return result with { CacheFile = File.Exists(path) ? path : null };
    }

    private const int LegacyMaxTokens = 400;

    private static string Hash(LlmRequest request)
    {
        var material = request.Model + "\u0001" +
                       request.Temperature.ToString("R", CultureInfo.InvariantCulture) + "\u0001" +
                       request.System + "\u0001" + request.User;
        var bytes = Encoding.UTF8.GetBytes(material);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void Save(string path, LlmRequest request, LlmResult result, long latencyMs)
    {
        try
        {
            var payload = new CacheFile
            {
                Request = new CacheRequest
                {
                    Model = request.Model,
                    Temperature = request.Temperature,
                    MaxTokens = request.MaxTokens,
                    System = request.System,
                    User = request.User
                },
                Response = result.Content,
                Usage = new CacheUsage
                {
                    PromptTokens = result.PromptTokens,
                    CompletionTokens = result.CompletionTokens,
                    CacheHitTokens = result.CacheHitTokens,
                    CacheMissTokens = result.CacheMissTokens
                },
                LatencyMs = latencyMs,
                Date = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            };
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, JsonSerializer.Serialize(payload, Options));
        }
        catch (IOException ex)
        {
            Log.Warn("Не вдалося зберегти кэш: " + ex.Message);
        }
    }

    private static LlmResult? Read(string path, out int maxTokens)
    {
        maxTokens = LegacyMaxTokens;
        try
        {
            var payload = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Options);
            if (payload is null)
            {
                return null;
            }
            maxTokens = payload.Request.MaxTokens;
            return new LlmResult(
                payload.Response,
                payload.Usage.PromptTokens,
                payload.Usage.CacheHitTokens,
                payload.Usage.CacheMissTokens,
                payload.Usage.CompletionTokens,
                payload.LatencyMs,
                true,
                null);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Warn("Пошкоджений файл кэшу " + path + ": " + ex.Message);
            return null;
        }
    }

    private sealed class CacheFile
    {
        public CacheRequest Request { get; set; } = new();

        public string Response { get; set; } = string.Empty;

        public CacheUsage Usage { get; set; } = new();

        public long LatencyMs { get; set; }

        public string Date { get; set; } = string.Empty;
    }

    private sealed class CacheRequest
    {
        public string Model { get; set; } = string.Empty;

        public double Temperature { get; set; }

        /// <summary>Лимит ответа при сохранении; в старых файлах кэша поля нет, там был 400.</summary>
        public int MaxTokens { get; set; } = LegacyMaxTokens;

        public string System { get; set; } = string.Empty;

        public string User { get; set; } = string.Empty;
    }

    private sealed class CacheUsage
    {
        public int PromptTokens { get; set; }

        public int CompletionTokens { get; set; }

        public int CacheHitTokens { get; set; }

        public int CacheMissTokens { get; set; }
    }
}
