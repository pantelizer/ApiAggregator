using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;


public sealed class GitHubApiOptions
{
    public const string SectionName = "ExternalApis:GitHub";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;


    public string? Token { get; set; }

    public string UserAgent { get; set; } = "ApiAggregator";

    public string DefaultQuery { get; set; } = "dotnet";

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [Range(0, 86400)]
    public int CacheTtlSeconds { get; set; } = 120;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}
