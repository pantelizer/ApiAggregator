using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public sealed class GitHubApiOptions : ExternalApiOptions
{
    public const string SectionName = "ExternalApis:GitHub";

    public GitHubApiOptions() => CacheTtlSeconds = 120;

    // The token is optional; it only raises the rate limit.
    protected override bool RequiresApiKey => false;

    public string UserAgent { get; set; } = "ApiAggregator";

    public string DefaultQuery { get; set; } = "dotnet";

    [Range(1, 100)]
    public int PageSize { get; set; } = 10;
}
