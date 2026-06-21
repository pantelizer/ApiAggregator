using System.Text.Json.Serialization;

namespace ApiAggregator.Models.External;

// DTOs mirroring the subset of the OpenWeatherMap "current weather" response we consume.

public sealed class WeatherResponse
{
    [JsonPropertyName("weather")]
    public List<WeatherCondition>? Weather { get; set; }

    [JsonPropertyName("main")]
    public WeatherMain? Main { get; set; }

    [JsonPropertyName("wind")]
    public WeatherWind? Wind { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("dt")]
    public long Dt { get; set; }

    [JsonPropertyName("sys")]
    public WeatherSys? Sys { get; set; }
}

public sealed class WeatherCondition
{
    [JsonPropertyName("main")]
    public string? Main { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class WeatherMain
{
    [JsonPropertyName("temp")]
    public double Temp { get; set; }

    [JsonPropertyName("feels_like")]
    public double FeelsLike { get; set; }

    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }
}

public sealed class WeatherWind
{
    [JsonPropertyName("speed")]
    public double Speed { get; set; }
}

public sealed class WeatherSys
{
    [JsonPropertyName("country")]
    public string? Country { get; set; }
}
