using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Svidok.Core.Config;
using Svidok.Core.Llm;

namespace Svidok.Llm;

/// <summary>Критическая ошибка клиента (нет ключа, нет баланса) — прогон нужно остановить.</summary>
public sealed class LlmFatalException : Exception
{
    public LlmFatalException(string message) : base(message)
    {
    }
}

/// <summary>Клиент DeepSeek API (раздел 10).</summary>
public sealed class DeepSeekClient : ILlmClient
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    private readonly LlmConfig _config;
    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly SemaphoreSlim _limit;

    public DeepSeekClient(LlmConfig config)
    {
        _config = config;
        _apiKey = Environment.GetEnvironmentVariable(config.ApiKeyEnv);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(5, config.TimeoutSec)) };
        _limit = new SemaphoreSlim(Math.Max(1, config.MaxConcurrent), Math.Max(1, config.MaxConcurrent));
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_apiKey);

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (!HasApiKey)
        {
            throw new LlmFatalException("Немає ключа API. Задайте змінну середовища " + _config.ApiKeyEnv + ".");
        }

        await _limit.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var payload = new
            {
                model = request.Model,
                messages = new[]
                {
                    new { role = "system", content = request.System },
                    new { role = "user", content = request.User }
                },
                temperature = request.Temperature,
                max_tokens = request.MaxTokens,
                response_format = new { type = "json_object" },
                stream = false
            };
            var body = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            var stopwatch = Stopwatch.StartNew();
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl());
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                message.Content = new StringContent(body, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested)
                {
                    return LlmResult.ErrorResult("таймаут запиту");
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= RetryDelays.Length)
                    {
                        return LlmResult.ErrorResult("мережа: " + ex.Message);
                    }
                    await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                    continue;
                }

                using (response)
                {
                    var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var status = response.StatusCode;
                    if (status is HttpStatusCode.Unauthorized or HttpStatusCode.PaymentRequired)
                    {
                        throw new LlmFatalException(status == HttpStatusCode.Unauthorized
                            ? "Немає ключа API або він недійсний"
                            : "Закінчився баланс DeepSeek");
                    }

                    if ((int)status == 429 || (int)status >= 500)
                    {
                        if (attempt >= RetryDelays.Length)
                        {
                            return LlmResult.ErrorResult("HTTP " + (int)status + ": " + Truncate(text, 200));
                        }
                        await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        return LlmResult.ErrorResult("HTTP " + (int)status + ": " + Truncate(text, 200));
                    }

                    stopwatch.Stop();
                    return ParseResponse(text, stopwatch.ElapsedMilliseconds);
                }
            }
        }
        finally
        {
            _limit.Release();
        }
    }

    private string BuildUrl()
    {
        var baseUrl = _config.BaseUrl.TrimEnd('/');
        return baseUrl + "/chat/completions";
    }

    private static LlmResult ParseResponse(string body, long latencyMs)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var content = string.Empty;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentElement))
                {
                    content = contentElement.GetString() ?? string.Empty;
                }
            }

            var promptTokens = 0;
            var completionTokens = 0;
            var cacheHit = 0;
            var cacheMiss = -1;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt))
                {
                    promptTokens = pt.GetInt32();
                }
                if (usage.TryGetProperty("completion_tokens", out var ct2))
                {
                    completionTokens = ct2.GetInt32();
                }
                if (usage.TryGetProperty("prompt_cache_hit_tokens", out var hit))
                {
                    cacheHit = hit.GetInt32();
                }
                if (usage.TryGetProperty("prompt_cache_miss_tokens", out var miss))
                {
                    cacheMiss = miss.GetInt32();
                }
            }

            if (cacheMiss < 0)
            {
                cacheMiss = Math.Max(0, promptTokens - cacheHit);
            }

            return new LlmResult(content, promptTokens, cacheHit, cacheMiss, completionTokens, latencyMs, false, null);
        }
        catch (JsonException ex)
        {
            return LlmResult.ErrorResult("відповідь не json: " + ex.Message);
        }
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
