namespace ApiAggregator.Models;

/// <summary>
/// Describes a detected performance anomaly: an API whose recent rolling-average response
/// </summary>
public sealed class AnomalyReport
{
    public required string ApiName { get; init; }

    public required double LifetimeAverageMs { get; init; }

    public required double RecentAverageMs { get; init; }

    public required long RecentSampleCount { get; init; }

    public required double Ratio { get; init; }
}
