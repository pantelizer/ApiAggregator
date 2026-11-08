namespace ApiAggregator.Configuration;

public sealed class WeatherApiOptions : ExternalApiOptions
{
    public const string SectionName = "ExternalApis:Weather";

    public string Units { get; set; } = "metric";

    public string DefaultCity { get; set; } = "London";
}
