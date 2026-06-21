using System.Net.Http.Json;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure;
using ApiAggregator.Models;
using ApiAggregator.Models.External;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Services.Providers;

/// <summary>
/// Provider for NewsAPI.org "/everything". Produces one normalized item per article.
/// </summary>
public sealed class NewsSourceProvider : SourceProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly NewsApiOptions _options;

    public NewsSourceProvider(
        HttpClient httpClient,
        IOptions<NewsApiOptions> options,
        IMemoryCache cache) : base(cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public override string SourceName => "News";

    protected override TimeSpan CacheTtl => TimeSpan.FromSeconds(_options.CacheTtlSeconds);

    protected override string BuildCacheKey(AggregationQuery query) =>
        (query.Keyword ?? _options.DefaultQuery).ToLowerInvariant();

    protected override async Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
        AggregationQuery query, CancellationToken cancellationToken)
    {
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? _options.DefaultQuery : query.Keyword;

        var url = $"everything?q={Uri.EscapeDataString(keyword)}" +
                  $"&pageSize={_options.PageSize}" +
                  $"&sortBy=relevancy" +
                  $"&apiKey={_options.ApiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
