using System.Text.Json.Serialization;

namespace ApiAggregator.Models.External;

// DTOs mirroring the subset of the NewsAPI "/everything" response we consume.

public sealed class NewsResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("articles")]
    public List<NewsArticle>? Articles { get; set; }
}

public sealed class NewsArticle
{
    [JsonPropertyName("source")]
    public NewsSource? Source { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class NewsSource
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
