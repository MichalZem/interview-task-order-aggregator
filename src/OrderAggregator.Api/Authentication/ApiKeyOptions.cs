using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Api.Authentication;

/// <summary>
/// Configurable list of accepted API keys. Bound from the <c>ApiKey</c>
/// section of configuration.
/// </summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    /// <summary>HTTP header that carries the key. Default <c>X-Api-Key</c>.</summary>
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>Accepted keys. At least one entry must be configured.</summary>
    [MinLength(1)]
    public List<ApiKeyEntry> Keys { get; set; } = new();
}

public sealed class ApiKeyEntry
{
    /// <summary>Human-readable label for the key (used in logs / claims).</summary>
    [Required]
    [StringLength(128, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The actual secret. Compared with ordinal, constant-time equality.</summary>
    [Required]
    [StringLength(256, MinimumLength = 8)]
    public string Key { get; set; } = string.Empty;
}
