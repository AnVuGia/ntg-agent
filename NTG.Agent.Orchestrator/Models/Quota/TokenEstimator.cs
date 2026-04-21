using Microsoft.ML.Tokenizers;

namespace NTG.Agent.Orchestrator.Services.Quota;

public static class TokenEstimator
{
    // GPT-4/GPT-3.5 use the cl100k_base encoding. 
    // This is a fast, offline way to estimate tokens
    private static readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel("gpt-4");

    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return _tokenizer.CountTokens(text);
    }
}