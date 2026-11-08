using System.Net;
using ApiAggregator.Configuration;
using ApiAggregator.Models;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace ApiAggregator.Tests;

public class NewsSourceProviderTests
{
    private const string SampleJson = """
    {
      "status": "ok",
      "totalResults": 2,
      "articles": [
        { "source": { "name": "Cat Times" }, "author": "A", "title": "Cats rule", "description": "d1",
          "url": "https://example.com/1", "publishedAt": "2026-11-07T09:00:00Z" },
        { "source": { "name": "Pet News" }, "author": null, "title": "More cats", "description": "d2",
          "url": "https://example.com/2", "publishedAt": "2026-11-06T09:00:00Z" }
      ]
    }
    """;

    private static (NewsSourceProvider Provider, List<Uri> RequestedUris) Build() => Build(_ => { });

    private static (NewsSourceProvider Provider, List<Uri> RequestedUris) Build(Action<HttpRequestMessage> inspect)
    {
        var requested = new List<Uri>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requested.Add(request.RequestUri!);
            inspect(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleJson, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://newsapi.org/v2/") };
        var options = Options.Create(new NewsApiOptions
        {
            BaseUrl = "https://newsapi.org/v2/",
            ApiKey = "test-key",
            DefaultQuery = "technology"
        });

        return (new NewsSourceProvider(httpClient, options, new FusionCache(new FusionCacheOptions())), requested);
    }

    private static AggregationQuery Query(string? newsQuery, string? keyword)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (newsQuery is not null)
        {
            parameters["newsQuery"] = newsQuery;
        }
        return new AggregationQuery { Keyword = keyword, Parameters = parameters };
    }

    [Theory]
    [InlineData("cats", "ai", "q=cats")]
    [InlineData(null, "ai", "q=ai")]
    [InlineData("", "ai", "q=ai")]
    [InlineData(null, null, "q=technology")]
    public async Task Search_term_is_newsQuery_then_keyword_then_default(
        string? newsQuery, string? keyword, string expectedInUrl)
    {
        var (provider, requested) = Build();

        await provider.FetchAsync(Query(newsQuery, keyword), CancellationToken.None);

        Assert.Contains(expectedInUrl, requested.Single().Query);
    }

    [Fact]
    public async Task Api_key_is_sent_in_the_header_not_the_url()
    {
        string? headerKey = null;
        var (provider, requested) = Build(request =>
            headerKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.Single() : null);

        await provider.FetchAsync(Query("cats", null), CancellationToken.None);

        Assert.Equal("test-key", headerKey);
        Assert.DoesNotContain("test-key", requested.Single().ToString());
        Assert.DoesNotContain("apiKey", requested.Single().Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Maps_articles_with_list_position_as_relevance()
    {
        var (provider, _) = Build();

        var result = await provider.FetchAsync(Query("cats", null), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Cats rule", result.Items[0].Title);
        Assert.Equal(2, result.Items[0].Relevance);
        Assert.Equal("Cat Times", result.Items[0].Extra!["publisher"]);
        Assert.Equal("n/a", result.Items[1].Extra!["author"]);
    }

    [Fact]
    public async Task Different_search_terms_are_cached_separately()
    {
        var (provider, requested) = Build();

        await provider.FetchAsync(Query("cats", null), CancellationToken.None);
        await provider.FetchAsync(Query("CATS", null), CancellationToken.None);
        await provider.FetchAsync(Query("dogs", null), CancellationToken.None);

        Assert.Equal(2, requested.Count);
    }
}
