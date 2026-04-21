namespace NTG.Agent.Orchestrator.Services.Quota;

public record QuotaCheckResult(
    bool IsAllowed, 
    int EstimatedTokens, 
    long RemainingTokens, 
    string? BlockReason
);