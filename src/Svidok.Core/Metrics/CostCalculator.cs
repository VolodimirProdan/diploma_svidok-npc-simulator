using Svidok.Core.Config;

namespace Svidok.Core.Metrics;

/// <summary>Стоимость запроса по ценам из конфига (раздел 10.6).</summary>
public static class CostCalculator
{
    public static double Compute(PriceTable prices, int cacheHitTokens, int cacheMissTokens, int completionTokens, DateTime utcNow)
    {
        var price = prices.For(utcNow);
        return ((cacheHitTokens * price.InputHit) +
                (cacheMissTokens * price.InputMiss) +
                (completionTokens * price.Output)) / 1_000_000.0;
    }
}
