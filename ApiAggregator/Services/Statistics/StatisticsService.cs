using System.Collections.Concurrent;
using ApiAggregator.Configuration;
using ApiAggregator.Models;
using Microsoft.Extensions.Options;

namespace ApiAggregator.Services.Statistics;

public sealed class StatisticsService : IStatisticsService
{
    private readonly ConcurrentDictionary<string, ApiStatEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly StatisticsOptions _options;
    private readonly TimeProvider _timeProvider;

    public StatisticsService(IOptions<StatisticsOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public void Record(string apiName, double elapsedMs, bool failed)
    {
        var entry = _entries.GetOrAdd(apiName, _ => new ApiStatEntry());
        entry.Record(elapsedMs, failed, _options.FastThresholdMs, _options.SlowThresholdMs, _timeProvider.GetUtcNow());
    }

    public IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots() =>
        _entries.Select(kvp => kvp.Value.Snapshot(kvp.Key)).ToList();

    public ApiStatisticsSnapshot? GetSnapshot(string apiName) =>
        _entries.TryGetValue(apiName, out var entry) ? entry.Snapshot(apiName) : null;

    public IReadOnlyList<AnomalyReport> DetectAnomalies()
    {
        var lookback = TimeSpan.FromMinutes(_options.AnomalyLookbackMinutes);
        var cutoff = _timeProvider.GetUtcNow() - lookback;
        var reports = new List<AnomalyReport>();

        foreach (var (name, entry) in _entries)
        {
            var (lifetimeAvg, recentAvg, recentCount) = entry.AveragesForWindow(cutoff);

            // Need a baseline and at least one recent sample to compare meaningfully.
            if (recentCount == 0 || lifetimeAvg <= 0)
            {
                continue;
            }

            var ratio = recentAvg / lifetimeAvg;
            if (ratio >= _options.AnomalyRatioThreshold)
            {
                reports.Add(new AnomalyReport
                {
                    ApiName = name,
                    LifetimeAverageMs = lifetimeAvg,
                    RecentAverageMs = recentAvg,
                    RecentSampleCount = recentCount,
                    Ratio = ratio
                });
            }
        }

        return reports;
    }

    /// <summary>One API's mutable counters, fully guarded by an internal lock.</summary>
    private sealed class ApiStatEntry
    {
        private readonly object _gate = new();
        private readonly Queue<Sample> _recent = new();

        private long _total;
        private long _failed;
        private double _totalMs;
        private double _min = double.MaxValue;
        private double _max;
        private long _fast;
        private long _average;
        private long _slow;

        public void Record(double elapsedMs, bool failed, int fastThreshold, int slowThreshold, DateTimeOffset now)
        {
            lock (_gate)
            {
                _total++;
                if (failed)
                {
                    _failed++;
                }

                _totalMs += elapsedMs;
                if (elapsedMs < _min) _min = elapsedMs;
                if (elapsedMs > _max) _max = elapsedMs;

                // Bucketing: fast < fastThreshold <= average <= slowThreshold < slow
                if (elapsedMs < fastThreshold) _fast++;
                else if (elapsedMs <= slowThreshold) _average++;
                else _slow++;

                _recent.Enqueue(new Sample(now, elapsedMs));
            }
        }

        public ApiStatisticsSnapshot Snapshot(string name)
        {
            lock (_gate)
            {
                return new ApiStatisticsSnapshot
                {
                    ApiName = name,
                    TotalRequests = _total,
                    FailedRequests = _failed,
                    AverageResponseTimeMs = _total == 0 ? 0 : _totalMs / _total,
                    MinResponseTimeMs = _total == 0 ? 0 : _min,
                    MaxResponseTimeMs = _max,
                    Buckets = new PerformanceBuckets { Fast = _fast, Average = _average, Slow = _slow }
                };
            }
        }

        public (double LifetimeAvg, double RecentAvg, long RecentCount) AveragesForWindow(DateTimeOffset cutoff)
        {
            lock (_gate)
            {
                while (_recent.Count > 0 && _recent.Peek().Timestamp < cutoff)
                {
                    _recent.Dequeue();
                }

                var lifetimeAvg = _total == 0 ? 0 : _totalMs / _total;

                double recentSum = 0;
                foreach (var sample in _recent)
                {
                    recentSum += sample.ElapsedMs;
                }

                var recentCount = _recent.Count;
                var recentAvg = recentCount == 0 ? 0 : recentSum / recentCount;
                return (lifetimeAvg, recentAvg, recentCount);
            }
        }
    }

    private readonly record struct Sample(DateTimeOffset Timestamp, double ElapsedMs);
}
