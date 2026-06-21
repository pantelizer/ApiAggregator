using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public sealed class WeatherApiOptions
{
    public const string SectionName = "ExternalApis:Weather";

    /// <summary>Base address of the API, e.g. https://api.openweathermap.org/data/2.5/.</summary>
    [Required]
    public string BaseUrl { get; set; } = string.Empty;


    [Required]
    public string ApiKey { get; set; } = string.Empty;

    public string Units { get; set; } = "metric";

    public string DefaultCity { get; set; } = "London";

    [Range(0, 86400)]
    public int CacheTtlSeconds { get; set; } = 60;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}
