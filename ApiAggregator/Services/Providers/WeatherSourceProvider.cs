using System.Net.Http.Json;
using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure;
using ApiAggregator.Models;
using ApiAggregator.Models.External;
using ZiggyCreatures.Caching.Fusion;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Services.Providers;

public sealed class WeatherSourceProvider : SourceProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly WeatherApiOptions _options;

    public WeatherSourceProvider(
        HttpClient httpClient,
        IOptions<WeatherApiOptions> options,
        IFusionCache cache) : base(cache)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public const string CityParameter = "city";

    public override string SourceName => "Weather";

    public override IReadOnlyList<SourceParameter> Parameters { get; } =
    [
        new(CityParameter, "City to get the current weather for. Defaults to ExternalApis:Weather:DefaultCity.", "Athens")
    ];

    protected override TimeSpan CacheTtl => TimeSpan.FromSeconds(_options.CacheTtlSeconds);

    protected override string BuildCacheKey(AggregationQuery query) => GetCity(query).ToLowerInvariant();

    private string GetCity(AggregationQuery query) => query.GetParameter(CityParameter) ?? _options.DefaultCity;

    protected override async Task<IReadOnlyList<AggregatedItem>> FetchFromSourceAsync(
        AggregationQuery query, CancellationToken cancellationToken)
    {
        var city = GetCity(query);

        var url = $"weather?q={Uri.EscapeDataString(city)}" +
                  $"&units={_options.Units}" +
                  $"&appid={_options.ApiKey}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Options.Set(StatisticsTrackingHandler.ApiNameKey, SourceName);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<WeatherResponse>(cancellationToken);
        if (dto is null)
        {
            return Array.Empty<AggregatedItem>();
        }

        var condition = dto.Weather?.FirstOrDefault();
        var observed = DateTimeOffset.FromUnixTimeSeconds(dto.Dt);

        var item = new AggregatedItem
        {
            Source = SourceName,
            Category = "weather",
            Title = $"Weather in {dto.Name ?? city}: {condition?.Main ?? "n/a"}",
            Description = condition?.Description,
            Url = null,
            Date = observed,
            Relevance = 0,
            Extra = new Dictionary<string, string>
            {
                ["temperature"] = dto.Main?.Temp.ToString("0.0") ?? "n/a",
                ["feelsLike"] = dto.Main?.FeelsLike.ToString("0.0") ?? "n/a",
                ["humidity"] = dto.Main?.Humidity.ToString() ?? "n/a",
                ["windSpeed"] = dto.Wind?.Speed.ToString("0.0") ?? "n/a",
                ["country"] = dto.Sys?.Country ?? "n/a"
            }
        };

        return new[] { item };
    }
}
