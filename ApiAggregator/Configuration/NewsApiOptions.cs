using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public sealed class NewsApiOptions : ExternalApiOptions
{
    public const string SectionName = "ExternalApis:News";

    public NewsApiOptions() => CacheTtlSeconds = 120;

    public string DefaultQuery { get; set; } = "technology";

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
