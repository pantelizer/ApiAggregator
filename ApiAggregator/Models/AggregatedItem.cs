namespace ApiAggregator.Models;


public sealed class AggregatedItem
{
    public required string Source { get; init; }

    public required string Category { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? Url { get; init; }


    public DateTimeOffset? Date { get; init; }


    public double Relevance { get; init; }

    public IReadOnlyDictionary<string, string>? Extra { get; init; }
}
