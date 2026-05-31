namespace OrderAggregator.Shared.Const;

/// <summary>
/// Cultures the API serves and the default one. Shared across the request-
/// localization setup and the OpenAPI culture parameter, so the language list
/// has a single source of truth. Strings themselves live in
/// <c>OrderAggregator.Resources</c> (strongly-typed <c>ApiMessages</c>).
/// </summary>
public static class LocalizationConstants
{
    /// <summary>
    /// Cultures the API serves. Index 0 is the default (neutral .resx = en).
    /// New language = add it here and ship the matching <c>ApiMessages.&lt;culture&gt;.resx</c>.
    /// </summary>
    public static readonly string[] SupportedCultures = ["en", "cs"];

    /// <summary>The default culture, served when the request asks for none we support.</summary>
    public static readonly string DefaultCulture = SupportedCultures[0];
}
