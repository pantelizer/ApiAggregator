using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;


public sealed class StatisticsOptions
{
    public const string SectionName = "Statistics";

    [Range(1, 100000)]
    public int FastThresholdMs { get; set; } = 100;

    [Range(1, 100000)]
    public int SlowThresholdMs { get; set; } = 200;

    [Range(5, 3600)]
    public int AnomalyCheckIntervalSeconds { get; set; } = 60;

    [Range(1, 60)]
    public int AnomalyLookbackMinutes { get; set; } = 5;


    [Range(1.0, 100.0)]
    public double AnomalyRatioThreshold { get; set; } = 1.5;
}
