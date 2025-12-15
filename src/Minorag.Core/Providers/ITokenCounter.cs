namespace Minorag.Core.Providers;

public interface ITokenCounter
{
    int CountTokens(string text);
}

/// <summary>
/// Conservative token estimator (not a real tokenizer).
/// Designed to avoid under-counting on punctuation-heavy / GUID / JSON / LICENSE text.
/// </summary>
public sealed class TokenCounter : ITokenCounter
{
    public int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var chars = text.Length;

        // Baseline: typical BPE token ~= 3-4 chars in English-ish text.
        var baseTokens = (chars + 2) / 3;

        // Extra pressure for punctuation-heavy content (JSON, sln, licenses, etc.)
        var punctuation = 0;
        foreach (var ch in text)
        {
            if (char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                punctuation++;
            }
        }

        // Punctuation inflates tokenization a lot on BPEs.
        var punctuationTokens = punctuation / 2;

        // Also count whitespace-delimited runs as a lower bound.
        var wsTokens = 0;
        var inToken = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inToken = false;
                continue;
            }

            if (!inToken)
            {
                wsTokens++;
                inToken = true;
            }
        }

        return Math.Max(wsTokens, baseTokens + punctuationTokens);
    }
}