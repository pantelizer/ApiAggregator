using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public sealed class CachingOptions
{
    public const string SectionName = "Caching";

    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]
    public TimeSpan FailSafeMaxDuration { get; set; } = TimeSpan.FromHours(1);

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan FailSafeThrottleDuration { get; set; } = TimeSpan.FromSeconds(30);

    [Range(typeof(TimeSpan), "00:00:00.010", "00:01:00")]
    public TimeSpan FactorySoftTimeout { get; set; } = TimeSpan.FromSeconds(1);

    // Empty = in-memory cache only.
    public string? RedisConnectionString { get; set; }

    public string RedisInstanceName { get; set; } = "ApiAggregator:";

    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan RedisCircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(10);

    [Range(typeof(TimeSpan), "00:00:00.050", "00:00:30")]
    public TimeSpan RedisOperationTimeout { get; set; } = TimeSpan.FromSeconds(1);
}
