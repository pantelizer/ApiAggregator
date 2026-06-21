using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public sealed class NewsApiOptions
{
    public const string SectionName = "ExternalApis:News";

    /// <summary>Base address, e.g. https://newsapi.org/v2/.</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string DefaultQuery { get; set; } = "technology";

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [Range(0, 86400)]
    public int CacheTtlSeconds { get; set; } = 120;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}
