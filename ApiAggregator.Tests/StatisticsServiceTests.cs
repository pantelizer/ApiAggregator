using ApiAggregator.Configuration;
using ApiAggregator.Services.Statistics;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Tests;

public class StatisticsServiceTests
{
    private static StatisticsService CreateService(MutableTimeProvider time, StatisticsOptions? options = null)
    {
        options ??= new StatisticsOptions
        {
            FastThresholdMs = 100,
            SlowThresholdMs = 200,
            AnomalyLookbackMinutes = 5,
            AnomalyRatioThreshold = 1.5
        };
        return new StatisticsService(Options.Create(options), time);
    }

    [Fact]
    public void Record_accumulates_count_and_average()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));

        service.Record("Weather", 100, failed: false);
        service.Record("Weather", 300, failed: true);

        var snapshot = service.GetSnapshot("Weather");

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.TotalRequests);
        Assert.Equal(1, snapshot.FailedRequests);
        Assert.Equal(200, snapshot.AverageResponseTimeMs); // (100 + 300) / 2
        Assert.Equal(100, snapshot.MinResponseTimeMs);
        Assert.Equal(300, snapshot.MaxResponseTimeMs);
    }

    [Fact]
    public void Record_places_requests_into_correct_buckets()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));

        service.Record("News", 50, false);    // fast  (< 100)
        service.Record("News", 100, false);   // average (>= 100 and <= 200)
        service.Record("News", 200, false);   // average (boundary, <= 200)
        service.Record("News", 250, false);   // slow  (> 200)

        var buckets = service.GetSnapshot("News")!.Buckets;

        Assert.Equal(1, buckets.Fast);
        Assert.Equal(2, buckets.Average);
        Assert.Equal(1, buckets.Slow);
    }

    [Fact]
    public void GetSnapshot_returns_null_for_unknown_api()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));
        Assert.Null(service.GetSnapshot("DoesNotExist"));
    }

    [Fact]
    public void DetectAnomalies_flags_api_whose_recent_average_exceeds_threshold()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var service = CreateService(time);

        // Baseline: 100 fast requests at 100ms each (these are "recent" only at the start).
        for (var i = 0; i < 100; i++)
        {
            service.Record("GitHub", 100, false);
        }

        // Move past the lookback window so the baseline samples age out.
        time.Advance(TimeSpan.FromMinutes(10));

        // Recent: a burst of slow requests.
        for (var i = 0; i < 5; i++)
        {
            service.Record("GitHub", 300, false);
        }

        var anomalies = service.DetectAnomalies();

        var gitHub = Assert.Single(anomalies);
        Assert.Equal("GitHub", gitHub.ApiName);
        Assert.Equal(300, gitHub.RecentAverageMs);     // only the recent slow samples
        Assert.True(gitHub.Ratio >= 1.5);              // recent (300) vs lifetime (~109)
    }

    [Fact]
    public void DetectAnomalies_returns_empty_when_performance_is_stable()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var service = CreateService(time);

        for (var i = 0; i < 20; i++)
        {
            service.Record("Weather", 100, false);
        }

        Assert.Empty(service.DetectAnomalies());
    }

    [Fact]
    public async Task Record_is_thread_safe_under_concurrent_writers()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));
        const int threads = 16;
        const int perThread = 1000;

        // Hammer the same API from many threads; all writes must be counted exactly once.
        var tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < perThread; i++)
            {
                service.Record("Weather", 10, false);
            }
        }));
        await Task.WhenAll(tasks);

        var snapshot = service.GetSnapshot("Weather");
        Assert.Equal(threads * perThread, snapshot!.TotalRequests);
    }
}
