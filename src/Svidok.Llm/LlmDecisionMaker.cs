using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Llm;

namespace Svidok.Llm;

/// <summary>Решатель V1–V4: промпт по методу, ответ модели, разбор и откат на V0 (разделы 9.5–9.7).</summary>
public sealed class LlmDecisionMaker : IPreparedDecisionMaker
{
    private const string RetrySuffix = "\nПоверни лише валідний json.";

    private readonly ILlmClient _client;
    private readonly PromptBuilder _builder;
    private readonly ActionCatalog _catalog;
    private readonly RuleDecisionMaker _fallback;
    private readonly Variant _variant;
    private readonly LlmConfig _config;

    public LlmDecisionMaker(
        ILlmClient client,
        PromptBuilder builder,
        ActionCatalog catalog,
        Variant variant,
        RuleDecisionMaker fallback,
        LlmConfig config)
    {
        _client = client;
        _builder = builder;
        _catalog = catalog;
        _variant = variant;
        _fallback = fallback;
        _config = config;
    }

    public Variant Variant => _variant;

    public PromptBuilder Builder => _builder;

    public PreparedPrompt Prepare(DecisionContext ctx) => _builder.Build(ctx);

    public Task<DecisionResult> DecideAsync(DecisionContext ctx, CancellationToken ct)
    {
        var prompt = Prepare(ctx);
        return DecidePreparedAsync(prompt, ctx, ct);
    }

    public async Task<DecisionResult> DecidePreparedAsync(PreparedPrompt prompt, DecisionContext ctx, CancellationToken ct)
    {
        // живой разговор в игре: выше температура, чтобы ответы не повторялись слово в слово
        var temperature = ctx.Interactive is null ? _config.Temperature : Math.Max(_config.Temperature, 0.9);
        var request = new LlmRequest(_config.Model, temperature, _config.MaxTokens, prompt.System, prompt.User);
        var result = await _client.CompleteAsync(request, ct).ConfigureAwait(false);

        var retried = false;
        if (result.Error is null && !string.IsNullOrWhiteSpace(result.Content) &&
            ResponseParser.TryParse(result.Content, _catalog, out var parsed, out _))
        {
            return Build(parsed, result, prompt, false);
        }

        if (result.Error is not null && result.Error.Contains("CacheMiss", StringComparison.Ordinal))
        {
            return Build(_fallback.Decide(ctx), result, prompt, false);
        }

        // повторный запрос с добавкой в конце промпта
        retried = true;
        var retryRequest = new LlmRequest(_config.Model, temperature, _config.MaxTokens, prompt.System, prompt.User + RetrySuffix);
        result = await _client.CompleteAsync(retryRequest, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.Content) &&
            ResponseParser.TryParse(result.Content, _catalog, out var second, out _))
        {
            return Build(second, result, prompt, false);
        }

        var fallback = _fallback.Decide(ctx);
        return Build(fallback, result, prompt, retried);
    }

    /// <summary>
    /// Произвольный запрос к той же модели (сжатие памяти NPC об игроке в окне игры).
    /// Возвращает текст ответа или null при ошибке.
    /// </summary>
    public async Task<string?> CompleteTextAsync(string system, string user, int maxTokens, CancellationToken ct)
    {
        var request = new LlmRequest(_config.Model, _config.Temperature, Math.Max(64, maxTokens), system, user);
        var result = await _client.CompleteAsync(request, ct).ConfigureAwait(false);
        return result.Error is null && !string.IsNullOrWhiteSpace(result.Content) ? result.Content : null;
    }

    private static DecisionResult Build(LlmDecision decision, LlmResult llm, PreparedPrompt prompt, bool parseError)
    {
        return new DecisionResult(
            decision,
            decision,
            ValidationResult.Success,
            llm,
            prompt.LocalTokens,
            parseError,
            prompt.System + "\n\n" + prompt.User,
            llm.Content);
    }
}

/// <summary>Создание клиента по режиму из конфига.</summary>
public static class LlmClientFactory
{
    public static ILlmClient Create(LlmConfig config, string? cacheDir = null)
    {
        var mode = config.ResolveMode();
        ILlmClient inner = mode == LlmMode.Mock
            ? new MockLlmClient()
            : new DeepSeekClient(config);

        return mode switch
        {
            LlmMode.Live => inner,
            LlmMode.Mock => inner,
            _ => new CachedLlmClient(inner, mode, cacheDir)
        };
    }

    public static bool HasApiKey(LlmConfig config) =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(config.ApiKeyEnv));
}
