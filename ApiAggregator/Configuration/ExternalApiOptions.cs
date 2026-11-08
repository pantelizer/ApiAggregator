using System.ComponentModel.DataAnnotations;

namespace ApiAggregator.Configuration;

public abstract class ExternalApiOptions : IValidatableObject
{
    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; }

    [Range(0, 86400)]
    public int CacheTtlSeconds { get; set; } = 60;

    // Per attempt; each retry gets its own timeout.
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 10;

    protected virtual bool RequiresApiKey => true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequiresApiKey && string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult("ApiKey is required for this API.", [nameof(ApiKey)]);
        }
    }
}
