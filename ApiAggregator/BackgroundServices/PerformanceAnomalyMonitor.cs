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
                    "Performance anomaly: {Api} recent avg {Recent:0.0}ms is {Pct:0}% above lifetime avg {Lifetime:0.0}ms (over last {Lookback}min, {Samples} samples).",
                    anomaly.ApiName,
                    anomaly.RecentAverageMs,
                    (anomaly.Ratio - 1) * 100,
                    anomaly.LifetimeAverageMs,
                    _options.AnomalyLookbackMinutes,
                    anomaly.RecentSampleCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while checking for performance anomalies.");
        }
    }
}
