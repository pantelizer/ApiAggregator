using System.Diagnostics;
using ApiAggregator.Services.Statistics;

namespace ApiAggregator.Infrastructure;


public sealed class StatisticsTrackingHandler : DelegatingHandler
{
    public static readonly HttpRequestOptionsKey<string> ApiNameKey = new("ApiName");

    private readonly IStatisticsService _statistics;

    public StatisticsTrackingHandler(IStatisticsService statistics)
    {
        _statistics = statistics;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiName = request.Options.TryGetValue(ApiNameKey, out var name)
            ? name
            : request.RequestUri?.Host ?? "unknown";

        var stopwatch = Stopwatch.StartNew();
        var failed = false;
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            failed = !response.IsSuccessStatusCode;
            return response;
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            _statistics.Record(apiName, stopwatch.Elapsed.TotalMilliseconds, failed);
        }
    }
}
