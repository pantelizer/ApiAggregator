using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApiAggregator.Models;

public enum SortField
{
    Date,
    Relevance,
    Source,
    Title
}

public enum SortDirection
{
    Ascending,
    Descending
}

public sealed class AggregationQuery
{
    /// <summary>Search term for sources that support searching (News, GitHub).</summary>
    public string? Keyword { get; set; }

    public string[]? Sources { get; set; }

    public string? Category { get; set; }

    public DateTimeOffset? FromDate { get; set; }

    public DateTimeOffset? ToDate { get; set; }

    public SortField SortBy { get; set; } = SortField.Relevance;

    public SortDirection SortDir { get; set; } = SortDirection.Descending;

    // Raw query-string values, filled by the controller.
    [BindNever]
    public IReadOnlyDictionary<string, string> Parameters { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? GetParameter(string name) =>
        Parameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    // Empty = all sources.
    public IReadOnlySet<string> GetRequestedSources() =>
        (Sources ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))   // "?Sources=" binds to [null]
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // A date-only value covers the whole UTC day; values with a time are used as given.
    public (DateTimeOffset? From, DateTimeOffset? To) GetDateRange()
    {
        var from = FromDate;
        var to = ToDate;

        if (TryGetDateOnly(nameof(FromDate), out var fromDay))
        {
            from = new DateTimeOffset(fromDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        if (TryGetDateOnly(nameof(ToDate), out var toDay))
        {
            to = new DateTimeOffset(toDay.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        }

        return (from, to);
    }

    private bool TryGetDateOnly(string name, out DateOnly date)
    {
        date = default;
        return GetParameter(name) is { } raw &&
               DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }
}
