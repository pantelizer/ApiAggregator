namespace ApiAggregator.Models;

/// <summary>Counts of requests falling into each performance bucket.</summary>
public sealed class PerformanceBuckets
{
    public long Fast { get; init; }

    public long Average { get; init; }

    public long Slow { get; init; }
}

/// <summary>
/// A read-only point-in-time view of one API's request statistics, returned by the
/// </summary>
public sealed class ApiStatisticsSnapshot
{
    public required string ApiName { get; init; }

    public required long TotalRequests { get; init; }

    public required long FailedRequests { get; init; }

    public required double AverageResponseTimeMs { get; init; }

    public double MinResponseTimeMs { get; init; }

    public double MaxResponseTimeMs { get; init; }

    public required PerformanceBuckets Buckets { get; init; }
}
