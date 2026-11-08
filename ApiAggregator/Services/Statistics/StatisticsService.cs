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
        _entries.Select(kvp => kvp.Value.Snapshot(kvp.Key, _options)).ToList();

    public ApiStatisticsSnapshot? GetSnapshot(string apiName) =>
        _entries.TryGetValue(apiName, out var entry) ? entry.Snapshot(apiName, _options) : null;

    public IReadOnlyList<AnomalyReport> DetectAnomalies()
    {
        var lookback = TimeSpan.FromMinutes(_options.AnomalyLookbackMinutes);
        var cutoff = _timeProvider.GetUtcNow() - lookback;
        var reports = new List<AnomalyReport>();

        foreach (var (name, entry) in _entries)
        {
            var window = entry.CompareWindow(cutoff);

            // Needs both recent and older samples (no baseline right after startup).
            if (window.RecentCount == 0 || window.BaselineCount == 0 || window.BaselineAvg <= 0)
            {
                continue;
            }

            var ratio = window.RecentAvg / window.BaselineAvg;
            if (ratio >= _options.AnomalyRatioThreshold)
            {
                reports.Add(new AnomalyReport
                {
                    ApiName = name,
                    BaselineAverageMs = window.BaselineAvg,
                    BaselineSampleCount = window.BaselineCount,
                    RecentAverageMs = window.RecentAvg,
                    RecentSampleCount = window.RecentCount,
                    Ratio = ratio
                });
            }
        }

        return reports;
    }

    private readonly record struct WindowComparison(
        double BaselineAvg, long BaselineCount, double RecentAvg, long RecentCount);

    private struct BucketCounter
    {
        public long Count;
        public double TotalMs;

        public void Add(double elapsedMs)
        {
            Count++;
            TotalMs += elapsedMs;
        }

        public readonly BucketStatistics ToStatistics(string range) => new()
        {
            Range = range,
            Count = Count,
            AverageResponseTimeMs = Count == 0 ? 0 : TotalMs / Count
        };
    }

    private sealed class ApiStatEntry
    {
        private readonly object _gate = new();
        private readonly Queue<Sample> _recent = new();

        private long _total;
        private long _failed;
        private double _totalMs;
        private double _min = double.MaxValue;
        private double _max;
        private BucketCounter _fast;
        private BucketCounter _average;
        private BucketCounter _slow;

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

                if (elapsedMs < fastThreshold) _fast.Add(elapsedMs);
                else if (elapsedMs <= slowThreshold) _average.Add(elapsedMs);
                else _slow.Add(elapsedMs);

                _recent.Enqueue(new Sample(now, elapsedMs));
            }
        }

        public ApiStatisticsSnapshot Snapshot(string name, StatisticsOptions options)
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
                    Buckets = new PerformanceBuckets
                    {
                        Fast = _fast.ToStatistics($"< {options.FastThresholdMs}ms"),
                        Average = _average.ToStatistics($"{options.FastThresholdMs}-{options.SlowThresholdMs}ms"),
                        Slow = _slow.ToStatistics($"> {options.SlowThresholdMs}ms")
                    }
                };
            }
        }

        public WindowComparison CompareWindow(DateTimeOffset cutoff)
        {
            lock (_gate)
            {
                // Keep only the samples inside the window.
                while (_recent.Count > 0 && _recent.Peek().Timestamp < cutoff)
                {
                    _recent.Dequeue();
                }

                double recentSum = 0;
                foreach (var sample in _recent)
                {
                    recentSum += sample.ElapsedMs;
                }

                long recentCount = _recent.Count;

                // Baseline = all samples minus the recent ones.
                var baselineCount = _total - recentCount;
                var baselineSum = _totalMs - recentSum;

                return new WindowComparison(
                    BaselineAvg: baselineCount == 0 ? 0 : baselineSum / baselineCount,
                    BaselineCount: baselineCount,
                    RecentAvg: recentCount == 0 ? 0 : recentSum / recentCount,
                    RecentCount: recentCount);
            }
        }
    }

    private readonly record struct Sample(DateTimeOffset Timestamp, double ElapsedMs);
}
