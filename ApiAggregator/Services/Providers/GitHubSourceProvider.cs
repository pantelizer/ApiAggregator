using System.Net.Http.Json;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure;
using ApiAggregator.Models;
using ApiAggregator.Models.External;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Services.Providers;

/// <summary>
/// Provider for the GitHub repository-search API. Produces one normalized item per repository.
/// </summary>
public sealed class GitHubSourceProvider : SourceProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly GitHubApiOptions _options;

    public GitHubSourceProvider(
        HttpClient httpClient,
        IOptions<GitHubApiOptions> options,
        IMemoryCache cache) : base(cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public override string SourceName => "GitHub";

    protected override TimeSpan CacheTtl => TimeSpan.FromSeconds(_options.CacheTtlSeconds);

    protected override string BuildCacheKey(AggregationQuery query) =>
        (query.Keyword ?? _options.DefaultQuery).ToLowerInvariant();

    protected override async Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
        AggregationQuery query, CancellationToken cancellationToken)
    {
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? _options.DefaultQuery : query.Keyword;

        var url = $"search/repositories?q={Uri.EscapeDataString(keyword)}" +
                  $"&sort=stars&order=desc&per_page={_options.PageSize}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(StatisticsTrackingHandler.ApiNameKey, SourceName);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<GitHubSearchResponse>(cancellationToken);
        if (dto?.Items is null || dto.Items.Count == 0)
        {
            return Array.Empty<AggregatedItem>();
        }

        return dto.Items.Select(repo => new AggregatedItem
        {
            Source = SourceName,
            Category = "repository",
            Title = repo.FullName ?? "(unknown repo)",
            Description = repo.Description,
            Url = repo.HtmlUrl,
            Date = repo.UpdatedAt,
            Relevance = repo.StargazersCount,
            Extra = new Dictionary<string, string>
            {
                ["stars"] = repo.StargazersCount.ToString(),
                ["language"] = repo.Language ?? "n/a"
            }
        }).ToList();
    }
}
