namespace NTG.Agent.Orchestrator.Models.Quota;

public class QuotaSettings
{
    public bool IsEnabled { get; set; } = true;
    public long MaxTokensAuth { get; set; } = 50000;
    public long MaxTokensAnonymous { get; set; } = 10000;
    public int ResetPeriodHours { get; set; } = 12;
}