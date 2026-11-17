namespace Svidok.Core.Knowledge;

/// <summary>Формулы достоверности (раздел 8).</summary>
public static class Credibility
{
    /// <summary>c_A = c_B × r(A→B).</summary>
    public static double Transfer(double sourceC, double trust) => Clamp(sourceC * trust);

    /// <summary>«Шумное ИЛИ» для независимых подтверждений: 1 − (1 − a)(1 − b).</summary>
    public static double NoisyOr(double a, double b) => Clamp(1.0 - ((1.0 - a) * (1.0 - b)));

    /// <summary>Забывание: C × exp(−λ × часы).</summary>
    public static double Forget(double c, double lambda, double hours) => Clamp(c * Math.Exp(-lambda * hours));

    public static double Clamp(double value) => Math.Clamp(value, 0.0, 1.0);

    public static double Round(double value, int digits = 4) => Math.Round(value, digits, MidpointRounding.AwayFromZero);
}
