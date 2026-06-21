namespace ApiAggregator.Models;

public enum SortField
{
    Date,
    Relevance,
    Source,
    Title
}

/// <summary>Sort direction.</summary>
public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// The inputs to an aggregation request: the search terms passed down to the providers,
/// </summary>
public sealed class AggregationQuery
{
    public string? City { get; set; }

    public string? Keyword { get; set; }

    public string[]? Sources { get; set; }

    public string? Category { get; set; }

    public DateTimeOffset? FromDate { get; set; }

    public DateTimeOffset? ToDate { get; set; }

    public SortField SortBy { get; set; } = SortField.Relevance;

    public SortDirection SortDir { get; set; } = SortDirection.Descending;
}
