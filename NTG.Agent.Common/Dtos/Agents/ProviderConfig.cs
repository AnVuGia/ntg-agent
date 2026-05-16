namespace NTG.Agent.Common.Dtos.Agents;

public record ProviderConfig(
    string? ProviderName,
    string? ProviderEndpoint,
    string? ProviderApiKey,
    string? ProviderModelName);
