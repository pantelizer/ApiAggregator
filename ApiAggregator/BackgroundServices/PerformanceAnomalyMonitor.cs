using ApiAggregator.Configuration;
using ApiAggregator.Services.Statistics;
using Microsoft.Extensions.Options;

namespace ApiAggregator.BackgroundServices;

public sealed class PerformanceAnomalyMonitor : BackgroundService
{
    private readonly IStatisticsService _statistics;
    private readonly StatisticsOptions _options;
    private readonly ILogger<PerformanceAnomalyMonitor> _logger;

    public PerformanceAnomalyMonitor(
        IStatisticsService statistics,
        IOptions<StatisticsOptions> options,
        ILogger<PerformanceAnomalyMonitor> logger)
    {
        _statistics = statistics;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.AnomalyCheckIntervalSeconds);
        _logger.LogInformation(
            "Performance anomaly monitor started (interval {Interval}s, lookback {Lookback}min, threshold {Ratio}x).",
            _options.AnomalyCheckIntervalSeconds, _options.AnomalyLookbackMinutes, _options.AnomalyRatioThreshold);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                CheckOnce();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CheckOnce()
    {
        try
        {
            var anomalies = _statistics.DetectAnomalies();
            foreach (var anomaly in anomalies)
            {
                _logger.LogWarning(
                    "Performance anomaly: {Api} averaged {Recent:0.0}ms over the last {Lookback}min ({Samples} requests), {Pct:0}% slower than its baseline of {Baseline:0.0}ms ({BaselineSamples} earlier requests).",
                    anomaly.ApiName,
                    anomaly.RecentAverageMs,
                    _options.AnomalyLookbackMinutes,
                    anomaly.RecentSampleCount,
                    (anomaly.Ratio - 1) * 100,
                    anomaly.BaselineAverageMs,
                    anomaly.BaselineSampleCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while checking for performance anomalies.");
        }
    }
}
