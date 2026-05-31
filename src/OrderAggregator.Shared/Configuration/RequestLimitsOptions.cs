using System.ComponentModel.DataAnnotations;

namespace OrderAggregator.Shared.Configuration;

/// <summary>
/// Inbound request guard rails. Bound from the <c>RequestLimits</c> section.
/// Caps the request body size so a single oversized payload can't exhaust
/// memory — the service is designed for many small batches, not huge ones.
/// </summary>
public sealed class RequestLimitsOptions
{
    public const string SectionName = "RequestLimits";

    /// <summary>
    /// Maximum accepted request body size in bytes. Requests larger than this
    /// are rejected by Kestrel with 413 Payload Too Large before model binding.
    /// Default 256 KiB — generous for a JSON array of { productId, quantity }.
    /// </summary>
    [Range(1024, 100 * 1024 * 1024)]
    public long MaxRequestBodyBytes { get; set; } = 256 * 1024;
}
