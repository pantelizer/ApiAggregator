using System.Net;
using ApiAggregator.Configuration;
using ApiAggregator.Models;
using ApiAggregator.Services.Providers;
using Microsoft.Extensions.Options;
using ZiggyCreatures.Caching.Fusion;

namespace ApiAggregator.Tests;

public class WeatherSourceProviderTests
{
    private const string SampleJson = """
    {
      "weather": [ { "main": "Clear", "description": "clear sky" } ],
      "main": { "temp": 21.5, "feels_like": 20.9, "humidity": 40 },
      "wind": { "speed": 3.1 },
      "name": "Athens",
      "dt": 1794054404,
      "sys": { "country": "GR" }
    }
    """;

    private static (WeatherSourceProvider Provider, List<Uri> RequestedUris) Build()
    {
        var requested = new List<Uri>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requested.Add(request.RequestUri!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(SampleJson, System.Text.Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openweathermap.org/data/2.5/") };
        var options = Options.Create(new WeatherApiOptions
        {
            BaseUrl = "https://api.openweathermap.org/data/2.5/",
            ApiKey = "test-key",
            DefaultCity = "London"
        });

        return (new WeatherSourceProvider(httpClient, options, new FusionCache(new FusionCacheOptions())), requested);
    }

    private static AggregationQuery QueryWith(params (string Key, string Value)[] parameters) => new()
    {
        Parameters = parameters.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase)
    };

    [Fact]
    public async Task Uses_the_city_parameter_from_the_query_string()
    {
        var (provider, requested) = Build();

        var result = await provider.FetchAsync(QueryWith(("City", "Athens")), CancellationToken.None);

        Assert.Contains("q=Athens", requested.Single().Query);
        var item = Assert.Single(result.Items);
        Assert.Equal("Weather in Athens: Clear", item.Title);
        Assert.Equal("GR", item.Extra!["country"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Falls_back_to_the_default_city(string? city)
    {
        var (provider, requested) = Build();
        var query = city is null ? QueryWith() : QueryWith(("city", city));

        await provider.FetchAsync(query, CancellationToken.None);

        Assert.Contains("q=London", requested.Single().Query);
    }

    [Fact]
    public async Task Different_cities_are_cached_separately()
    {
        var (provider, requested) = Build();

        await provider.FetchAsync(QueryWith(("city", "Athens")), CancellationToken.None);
        await provider.FetchAsync(QueryWith(("city", "Athens")), CancellationToken.None);
        await provider.FetchAsync(QueryWith(("city", "Berlin")), CancellationToken.None);

        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public void Declares_its_city_parameter()
    {
        var (provider, _) = Build();

        var parameter = Assert.Single(provider.Parameters);
        Assert.Equal("city", parameter.Name);
    }
}
