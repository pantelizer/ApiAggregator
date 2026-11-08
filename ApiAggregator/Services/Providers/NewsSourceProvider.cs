using System.Net.Http.Json;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure;
using ApiAggregator.Models;
using ApiAggregator.Models.External;
using ZiggyCreatures.Caching.Fusion;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Services.Providers;

public sealed class NewsSourceProvider : SourceProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly NewsApiOptions _options;

    public NewsSourceProvider(
        HttpClient httpClient,
        IOptions<NewsApiOptions> options,
        IFusionCache cache) : base(cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public const string QueryParameter = "newsQuery";

    public override string SourceName => "News";

    public override IReadOnlyList<SourceParameter> Parameters { get; } =
    [
        new(QueryParameter,
            "Search term for news only. Overrides keyword for this source; defaults to keyword, then ExternalApis:News:DefaultQuery.",
            "cats")
    ];

    protected override TimeSpan CacheTtl => TimeSpan.FromSeconds(_options.CacheTtlSeconds);

    protected override string BuildCacheKey(AggregationQuery query) => GetSearchTerm(query).ToLowerInvariant();

    // newsQuery, then keyword, then DefaultQuery.
    private string GetSearchTerm(AggregationQuery query) =>
        query.GetParameter(QueryParameter)
        ?? (string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim())
        ?? _options.DefaultQuery;

    protected override async Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
        AggregationQuery query, CancellationToken cancellationToken)
    {
        var keyword = GetSearchTerm(query);

        var url = $"everything?q={Uri.EscapeDataString(keyword)}" +
                  $"&pageSize={_options.PageSize}" +
                  $"&sortBy=relevancy";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Api-Key", _options.ApiKey);
        request.Options.Set(StatisticsTrackingHandler.ApiNameKey, SourceName);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<NewsResponse>(cancellationToken);
        if (dto?.Articles is null || dto.Articles.Count == 0)
        {
            return Array.Empty<AggregatedItem>();
        }

        var total = dto.Articles.Count;
        var items = new List<AggregatedItem>(total);
        for (var i = 0; i < total; i++)
        {
            var article = dto.Articles[i];
            items.Add(new AggregatedItem
            {
                Source = SourceName,
                Category = "news",
                Title = article.Title ?? "(untitled)",
                Description = article.Description,
                Url = article.Url,
                Date = article.PublishedAt,
                Relevance = total - i,
                Extra = new Dictionary<string, string>
                {
                    ["author"] = article.Author ?? "n/a",
                    ["publisher"] = article.Source?.Name ?? "n/a"
                }
            });
        }

        return items;
    }
}
