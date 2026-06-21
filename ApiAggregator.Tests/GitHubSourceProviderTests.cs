using System.Net;
using ApiAggregator.Configuration;
using ApiAggregator.Models;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Tests;

public class GitHubSourceProviderTests
{
    private const string SampleJson = """
    {
      "total_count": 2,
      "items": [
        {
          "full_name": "dotnet/runtime",
          "description": ".NET runtime",
          "html_url": "https://github.com/dotnet/runtime",
          "stargazers_count": 15000,
          "language": "C#",
          "updated_at": "2024-05-01T10:00:00Z"
        },
        {
          "full_name": "dotnet/aspnetcore",
          "description": "ASP.NET Core",
          "html_url": "https://github.com/dotnet/aspnetcore",
          "stargazers_count": 9000,
          "language": "C#",
          "updated_at": "2024-04-01T10:00:00Z"
        }
      ]
    }
    """;

    [Fact]
    public async Task Maps_repositories_into_normalized_items_with_stars_as_relevance()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, SampleJson);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(new GitHubApiOptions
        {
            BaseUrl = "https://api.github.com/",
            DefaultQuery = "dotnet",
            PageSize = 20
        });

        var provider = new GitHubSourceProvider(httpClient, options, new MemoryCache(new MemoryCacheOptions()));

        var result = await provider.FetchAsync(new AggregationQuery { Keyword = "dotnet" }, CancellationToken.None);

        Assert.Equal(2, result.Items.Count);

        var runtime = result.Items[0];
        Assert.Equal("GitHub", runtime.Source);
        Assert.Equal("repository", runtime.Category);
        Assert.Equal("dotnet/runtime", runtime.Title);
        Assert.Equal(15000, runtime.Relevance);          // stars become relevance
        Assert.Equal("https://github.com/dotnet/runtime", runtime.Url);
        Assert.Equal("C#", runtime.Extra!["language"]);
    }

    [Fact]
    public async Task Non_success_status_throws_so_aggregation_can_isolate_it()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        var options = Options.Create(new GitHubApiOptions { BaseUrl = "https://api.github.com/" });

        var provider = new GitHubSourceProvider(httpClient, options, new MemoryCache(new MemoryCacheOptions()));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.FetchAsync(new AggregationQuery { Keyword = "x" }, CancellationToken.None));
    }
}
