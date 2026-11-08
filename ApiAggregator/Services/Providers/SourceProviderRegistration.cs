using ApiAggregator.Configuration;
using ApiAggregator.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Services.Providers;

public static class SourceProviderRegistration
{
    public static IHttpClientBuilder AddSourceProvider<TProvider, TOptions>(
        this IServiceCollection services,
        string sectionName,
        Action<HttpClient, TOptions>? configureClient = null)
        where TProvider : class, ISourceProvider
        where TOptions : ExternalApiOptions
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTransient<StatisticsTrackingHandler>();

        var clientBuilder = services.AddHttpClient<TProvider>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl);
            configureClient?.Invoke(client, options);
        });

        // Resilience wraps the statistics handler, so each retry attempt is timed separately.
        clientBuilder.AddStandardResilienceHandler().Configure((resilience, sp) =>
        {
            var options = sp.GetRequiredService<IOptions<TOptions>>().Value;
            var attempt = TimeSpan.FromSeconds(options.TimeoutSeconds);

            resilience.AttemptTimeout.Timeout = attempt;
            resilience.TotalRequestTimeout.Timeout = attempt * (resilience.Retry.MaxRetryAttempts + 1);
            resilience.CircuitBreaker.SamplingDuration = attempt * 2;
        });
        clientBuilder.AddHttpMessageHandler<StatisticsTrackingHandler>();

        services.AddTransient<ISourceProvider>(sp => sp.GetRequiredService<TProvider>());

        return clientBuilder;
    }
}
