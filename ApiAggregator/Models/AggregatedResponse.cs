namespace ApiAggregator.Models;

public sealed class SourceStatus
{
    public required string Source { get; init; }

    public required bool Succeeded { get; init; }

    public required int ItemCount { get; init; }

    public bool FromCache { get; init; }

    public string? Error { get; init; }
}


public sealed class AggregatedResponse
{
    public required IReadOnlyList<AggregatedItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required IReadOnlyList<SourceStatus> Sources { get; init; }

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
