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
        Assert.Equal(200, snapshot.AverageResponseTimeMs);
        Assert.Equal(100, snapshot.MinResponseTimeMs);
        Assert.Equal(300, snapshot.MaxResponseTimeMs);
    }

    [Fact]
    public void Record_places_requests_into_correct_buckets()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));

        service.Record("News", 50, false);
        service.Record("News", 100, false);
        service.Record("News", 200, false);
        service.Record("News", 250, false);

        var buckets = service.GetSnapshot("News")!.Buckets;

        Assert.Equal(1, buckets.Fast.Count);
        Assert.Equal(2, buckets.Average.Count);
        Assert.Equal(1, buckets.Slow.Count);
    }

    [Fact]
    public void Each_bucket_reports_its_own_average_and_range()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));

        service.Record("News", 40, false);
        service.Record("News", 60, false);
        service.Record("News", 120, false);
        service.Record("News", 180, false);
        service.Record("News", 900, false);

        var buckets = service.GetSnapshot("News")!.Buckets;

        Assert.Equal(50, buckets.Fast.AverageResponseTimeMs);
        Assert.Equal(150, buckets.Average.AverageResponseTimeMs);
        Assert.Equal(900, buckets.Slow.AverageResponseTimeMs);
        Assert.Equal("< 100ms", buckets.Fast.Range);
        Assert.Equal("100-200ms", buckets.Average.Range);
        Assert.Equal("> 200ms", buckets.Slow.Range);
    }

    [Fact]
    public void Empty_bucket_has_zero_average()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));

        service.Record("News", 50, false);

        var slow = service.GetSnapshot("News")!.Buckets.Slow;
        Assert.Equal(0, slow.Count);
        Assert.Equal(0, slow.AverageResponseTimeMs);
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

        for (var i = 0; i < 100; i++)
        {
            service.Record("GitHub", 100, false);
        }

        time.Advance(TimeSpan.FromMinutes(10));

        for (var i = 0; i < 5; i++)
        {
            service.Record("GitHub", 300, false);
        }

        var anomalies = service.DetectAnomalies();

        var gitHub = Assert.Single(anomalies);
        Assert.Equal("GitHub", gitHub.ApiName);
        Assert.Equal(300, gitHub.RecentAverageMs);
        Assert.Equal(100, gitHub.BaselineAverageMs);
        Assert.Equal(100, gitHub.BaselineSampleCount);
        Assert.Equal(3, gitHub.Ratio);
    }

    [Fact]
    public void DetectAnomalies_compares_against_older_samples_only()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var service = CreateService(time);

        for (var i = 0; i < 10; i++)
        {
            service.Record("News", 100, false);
        }

        time.Advance(TimeSpan.FromMinutes(10));

        for (var i = 0; i < 30; i++)
        {
            service.Record("News", 160, false);
        }

        var news = Assert.Single(service.DetectAnomalies());
        Assert.Equal(100, news.BaselineAverageMs);
        Assert.Equal(160, news.RecentAverageMs);
        Assert.Equal(1.6, news.Ratio, precision: 10);
    }

    [Fact]
    public void DetectAnomalies_needs_older_samples_before_reporting()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));

        for (var i = 0; i < 5; i++)
        {
            service.Record("Weather", 900, false);
        }

        Assert.Empty(service.DetectAnomalies());
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

        time.Advance(TimeSpan.FromMinutes(10));

        for (var i = 0; i < 20; i++)
        {
            service.Record("Weather", 110, false);
        }

        Assert.Empty(service.DetectAnomalies());
    }

    [Fact]
    public async Task Record_is_thread_safe_under_concurrent_writers()
    {
        var service = CreateService(new MutableTimeProvider(DateTimeOffset.UnixEpoch));
        const int threads = 16;
        const int perThread = 1000;

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
