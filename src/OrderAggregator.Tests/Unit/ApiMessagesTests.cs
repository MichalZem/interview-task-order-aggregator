using System.Globalization;
using OrderAggregator.Resources;

namespace OrderAggregator.Tests.Unit;

/// <summary>
/// Strongly-typed localization: the source-generated <see cref="ApiMessages"/>
/// class exposes each resource key as a compile-time-checked property and
/// resolves the value via ResourceManager against the ambient UI culture. These
/// tests prove the neutral (en) resource and the Czech satellite assembly both
/// load and that culture selection actually switches the returned text.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public class ApiMessagesTests
{
    [Fact]
    public void NeutralCulture_ReturnsEnglish()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            // Invariant falls back to the neutral .resx (English).
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            Assert.Equal("Request body must contain at least one order.", ApiMessages.OrderBatchEmpty);
            Assert.Equal("Unknown productId '{0}'. Order rejected.", ApiMessages.OrderUnknownProduct);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void CzechCulture_ReturnsCzech_FromSatelliteAssembly()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("cs");

            Assert.Equal("Tělo požadavku musí obsahovat alespoň jednu objednávku.", ApiMessages.OrderBatchEmpty);
            Assert.Equal("Neznámé productId '{0}'. Objednávka odmítnuta.", ApiMessages.OrderUnknownProduct);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Property_IsCompileTimeChecked_AndNonNull()
    {
        // The generated property returns a non-null string (NullForgivingOperators),
        // so call-sites don't juggle string?. Referencing it by name at all is the
        // strong-typing guarantee — a renamed/removed key fails the build, not a
        // runtime lookup. Pin invariant so the assertion is culture-independent.
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            string message = ApiMessages.OrderInvalidValue;
            Assert.False(string.IsNullOrEmpty(message));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
