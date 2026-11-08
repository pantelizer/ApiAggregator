using ApiAggregator.Configuration;
using ApiAggregator.Services.Providers;
using ApiAggregator.Services.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Moq;

namespace ApiAggregator.Tests;

public class SourceProviderRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings, Action<IServiceCollection> register)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddFusionCache();
        services.AddSingleton(Mock.Of<IStatisticsService>());
        register(services);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> WeatherSettings(string? apiKey = "test-key", string timeout = "7") => new()
    {
        ["ExternalApis:Weather:BaseUrl"] = "https://api.openweathermap.org/data/2.5/",
        ["ExternalApis:Weather:ApiKey"] = apiKey,
        ["ExternalApis:Weather:TimeoutSeconds"] = timeout
    };

    [Fact]
    public void Registered_provider_is_discovered_as_a_source_with_its_base_address()
    {
        using var sp = Build(WeatherSettings(), s =>
            s.AddSourceProvider<WeatherSourceProvider, WeatherApiOptions>(WeatherApiOptions.SectionName));

        var source = Assert.Single(sp.GetServices<ISourceProvider>());
        Assert.IsType<WeatherSourceProvider>(source);

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(WeatherSourceProvider));
        Assert.Equal(new Uri("https://api.openweathermap.org/data/2.5/"), client.BaseAddress);
    }

    [Fact]
    public void Timeouts_come_from_the_options()
    {
        using var sp = Build(WeatherSettings(timeout: "7"), s =>
            s.AddSourceProvider<WeatherSourceProvider, WeatherApiOptions>(WeatherApiOptions.SectionName));

        var resilience = sp.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{nameof(WeatherSourceProvider)}-standard");

        Assert.Equal(TimeSpan.FromSeconds(7), resilience.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(7 * (resilience.Retry.MaxRetryAttempts + 1)), resilience.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(14), resilience.CircuitBreaker.SamplingDuration);
    }

    [Fact]
    public void Missing_api_key_fails_validation()
    {
        using var sp = Build(WeatherSettings(apiKey: null), s =>
            s.AddSourceProvider<WeatherSourceProvider, WeatherApiOptions>(WeatherApiOptions.SectionName));

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<WeatherApiOptions>>().Value);
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void GitHub_works_without_an_api_key()
    {
        var settings = new Dictionary<string, string?> { ["ExternalApis:GitHub:BaseUrl"] = "https://api.github.com/" };
        using var sp = Build(settings, s =>
            s.AddSourceProvider<GitHubSourceProvider, GitHubApiOptions>(GitHubApiOptions.SectionName));

        var options = sp.GetRequiredService<IOptions<GitHubApiOptions>>().Value;

        Assert.Null(options.ApiKey);
        Assert.Equal(120, options.CacheTtlSeconds);
    }

    [Fact]
    public void Relative_base_url_fails_validation()
    {
        var settings = WeatherSettings();
        settings["ExternalApis:Weather:BaseUrl"] = "api.openweathermap.org";
        using var sp = Build(settings, s =>
            s.AddSourceProvider<WeatherSourceProvider, WeatherApiOptions>(WeatherApiOptions.SectionName));

        var ex = Assert.Throws<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<WeatherApiOptions>>().Value);
        Assert.Contains("BaseUrl", ex.Message);
    }
}
