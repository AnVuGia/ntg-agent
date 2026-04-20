using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Web.Google;
using Microsoft.Extensions.Logging;

namespace NTG.Agent.AITools.SearchOnlineTool.Services;

public class GoogleTextSearchService : ITextSearchService
{
    private readonly ITextSearch _googleTextSearch;
    private readonly ILogger<GoogleTextSearchService> _logger;

    public GoogleTextSearchService(ITextSearch googleTextSearch, ILogger<GoogleTextSearchService> logger)
    {
        _googleTextSearch = googleTextSearch ?? throw new ArgumentNullException(nameof(googleTextSearch));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<TextSearchResult> SearchAsync(string query, int top)
    {
        _logger.LogInformation("[INTERNET SEARCH] Model is hitting the web for query: '{Query}'", query);

        var results = await _googleTextSearch.GetTextSearchResultsAsync(query, new() { Top = top });

        _logger.LogDebug("[INTERNET SEARCH] Successfully retrieved web results.");

        await foreach (var result in results.Results)
        {
            yield return result;
        }
    }
}
