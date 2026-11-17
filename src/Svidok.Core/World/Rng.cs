namespace Svidok.Core.WorldModel;

/// <summary>
/// Три независимых генератора случайных чисел мира (раздел 5.3 ТЗ).
/// Random.Shared, Guid.NewGuid и текущее время внутри Core запрещены.
/// </summary>
public sealed class Rng
{
    public Rng(int seed)
    {
        Seed = seed;
        Sim = new Random(seed);
        Rumor = new Random(unchecked(seed + 1));
        Filler = new Random(unchecked(seed + 2));
    }

    public int Seed { get; }

    /// <summary>Основная случайность симуляции: движение, искажения полей, доверие.</summary>
    public Random Sim { get; }

    /// <summary>Случайность распространения слухов.</summary>
    public Random Rumor { get; }

    /// <summary>Случайность фоновых событий.</summary>
    public Random Filler { get; }

    public double NextDouble(Random random) => random.NextDouble();

    public bool Chance(double probability, Random? random = null)
    {
        if (probability <= 0)
        {
            return false;
        }
        if (probability >= 1)
        {
            return true;
        }
        return (random ?? Sim).NextDouble() < probability;
    }

    public int NextInt(int minInclusive, int maxExclusive, Random? random = null)
    {
        var r = random ?? Sim;
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }
        return r.Next(minInclusive, maxExclusive);
    }

    public double NextRange(double min, double max, Random? random = null)
    {
        return min + (random ?? Sim).NextDouble() * (max - min);
    }

    public T Pick<T>(IReadOnlyList<T> items, Random? random = null)
    {
        return items[(random ?? Sim).Next(items.Count)];
    }

    public T? PickOrDefault<T>(IReadOnlyList<T> items, Random? random = null)
    {
        return items.Count == 0 ? default : items[(random ?? Sim).Next(items.Count)];
    }

    /// <summary>Пуассон по алгоритму Кнута.</summary>
    public int Poisson(double lambda, Random? random = null)
    {
        if (lambda <= 0)
        {
            return 0;
        }
        var r = random ?? Sim;
        var limit = Math.Exp(-lambda);
        var product = 1.0;
        var count = 0;
        do
        {
            count++;
            product *= r.NextDouble();
        }
        while (product > limit);
        return count - 1;
    }

    public void Shuffle<T>(IList<T> list, Random? random = null)
    {
        var r = random ?? Sim;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = r.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Взвешенный выбор: возвращает индекс.</summary>
    public int PickWeighted(IReadOnlyList<double> weights, Random? random = null)
    {
        var r = random ?? Sim;
        var total = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            total += weights[i];
        }
        if (total <= 0)
        {
            return 0;
        }
        var roll = r.NextDouble() * total;
        var acc = 0.0;
        for (var i = 0; i < weights.Count; i++)
        {
            acc += weights[i];
            if (roll <= acc)
            {
                return i;
            }
        }
        return weights.Count - 1;
    }
}
