using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NTG.Agent.Orchestrator.Models.Quota;
using NTG.Agent.Orchestrator.Services.TokenTracking;

namespace NTG.Agent.Orchestrator.Services.Quota;

public interface IUserQuotaService
{
    Task<QuotaCheckResult> CheckQuotaAsync(Guid? userId, Guid? sessionId, string promptText);
}

public class UserQuotaService : IUserQuotaService
{
    private readonly ITokenTrackingService _tokenTrackingService;
    private readonly QuotaSettings _settings;
    private readonly ILogger<UserQuotaService> _logger;

    public UserQuotaService(
        ITokenTrackingService tokenTrackingService, 
        IOptions<QuotaSettings> settings,
        ILogger<UserQuotaService> logger) 
    {
        _tokenTrackingService = tokenTrackingService;
        _settings = settings.Value;
        _logger = logger; 
    }

    public async Task<QuotaCheckResult> CheckQuotaAsync(Guid? userId, Guid? sessionId, string promptText)
    {
        if (!_settings.IsEnabled) 
            return new QuotaCheckResult(true, 0, long.MaxValue, null);

        // 1. Estimate the cost of the incoming prompt
        int estimatedPromptTokens = TokenEstimator.EstimateTokens(promptText);

        // 2. Define the rolling window (e.g., the last 12 hours)
        var fromDate = DateTime.UtcNow.AddHours(-_settings.ResetPeriodHours);

        // 3. Get actual usage strictly within that window
        var stats = await _tokenTrackingService.GetUsageStatsAsync(
            userId: userId, 
            sessionId: sessionId, 
            fromDate: fromDate, 
            toDate: DateTime.UtcNow);

        // 4. Determine which limit applies
        bool isAnonymous = !userId.HasValue;
        long maxTokens = isAnonymous ? _settings.MaxTokensAnonymous : _settings.MaxTokensAuth;
        long tokensUsedInWindow = stats?.TotalTokens ?? 0L;
        long remainingTokens = maxTokens - tokensUsedInWindow;

        // 5. Check if the prompt pushes them over the limit
        if (estimatedPromptTokens > remainingTokens)
        {
            // Log warning when they are blocked
            _logger.LogWarning(
                "QUOTA BLOCKED: User {UserId} / Session {SessionId} attempted {Estimated} tokens, but only has {Remaining} left.",
                userId, sessionId, estimatedPromptTokens, remainingTokens);

            return new QuotaCheckResult(
                IsAllowed: false, 
                EstimatedTokens: estimatedPromptTokens, 
                RemainingTokens: remainingTokens < 0 ? 0 : remainingTokens, 
                BlockReason: "quota_exhausted"
            );
        }

        // Log remaining balance when they are allowed
        _logger.LogInformation(
            "QUOTA CHECK: User {UserId} has {RemainingTokens} tokens remaining in their rolling window.", 
            userId, remainingTokens);

        return new QuotaCheckResult(true, estimatedPromptTokens, remainingTokens, null);
    }
}