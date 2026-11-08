namespace ApiAggregator.Models;

public sealed class SourceInfo
{
    public required string Name { get; init; }

    public required IReadOnlyList<SourceParameter> Parameters { get; init; }
}
