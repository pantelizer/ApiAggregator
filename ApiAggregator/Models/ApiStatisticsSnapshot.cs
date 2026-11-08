namespace ApiAggregator.Models;

public sealed class BucketStatistics
{
    public required string Range { get; init; }

    public required long Count { get; init; }

    public required double AverageResponseTimeMs { get; init; }
}

public sealed class PerformanceBuckets
{
    public required BucketStatistics Fast { get; init; }

    public required BucketStatistics Average { get; init; }

    public required BucketStatistics Slow { get; init; }
}

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
