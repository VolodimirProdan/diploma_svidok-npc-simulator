using Microsoft.ML.Tokenizers;

namespace Svidok.Llm;

/// <summary>Локальный счётчик токенов (Microsoft.ML.Tokenizers, o200k_base) с калибровкой по usage API.</summary>
public sealed class TokenCounter
{
    private readonly Tokenizer _tokenizer;

    public TokenCounter()
    {
        _tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");
    }

    /// <summary>Коэффициент калибровки k = usage.prompt_tokens / localCount.</summary>
    public double Calibration { get; set; } = 1.0;

    public int Count(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        return (int)_tokenizer.CountTokens(text);
    }

    /// <summary>Оценка «сколько было бы токенов»: localCount × k.</summary>
    public int Estimate(string text) => (int)Math.Round(Count(text) * Calibration);

    public void Observe(int localCount, int apiPromptTokens)
    {
        if (localCount <= 0 || apiPromptTokens <= 0)
        {
            return;
        }
        Calibration = (double)apiPromptTokens / localCount;
    }
}
