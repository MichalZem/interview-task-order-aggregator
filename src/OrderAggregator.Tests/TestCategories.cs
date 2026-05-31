namespace OrderAggregator.Tests;

/// <summary>
/// Trait categories for grouping/filtering tests. Both Test Explorer
/// (Group by → Traits) and the CLI (<c>dotnet test --filter Category=Unit</c>)
/// read them. Constants instead of magic strings — a typo would otherwise
/// silently spawn a new group.
/// </summary>
public static class TestCategories
{
    public const string Name = "Category";

    public const string Unit = "Unit";
    public const string Integration = "Integration";
    public const string Architecture = "Architecture";
}
