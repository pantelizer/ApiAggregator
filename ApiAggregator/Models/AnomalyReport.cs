namespace ApiAggregator.Models;

public sealed class AnomalyReport
{
    public required string ApiName { get; init; }

    public required double BaselineAverageMs { get; init; }

    public required long BaselineSampleCount { get; init; }

    public required double RecentAverageMs { get; init; }

    public required long RecentSampleCount { get; init; }

    public required double Ratio { get; init; }
}
