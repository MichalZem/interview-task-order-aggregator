using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using OrderAggregator.Abstractions;
using OrderAggregator.Contracts.Orders;
using OrderAggregator.Shared.Configuration;
using OrderAggregator.Models;
using OrderAggregator.Resources;
using OrderAggregator.Services.Stores;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace OrderAggregator.Tests;

[Trait(TestCategories.Name, TestCategories.Architecture)]
public class ArchitectureTests
{
    // One Architecture instance shared across tests — loading takes a few hundred ms.
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Order).Assembly,                // OrderAggregator.Models
            typeof(IProductRepository).Assembly,   // OrderAggregator.Abstractions
            typeof(AggregationOptions).Assembly,   // OrderAggregator.Shared
            typeof(InMemoryOrderStore).Assembly,   // OrderAggregator.Services
            typeof(OrderRequest).Assembly,         // OrderAggregator.Contracts
            typeof(ApiMessages).Assembly,          // OrderAggregator.Resources
            typeof(Program).Assembly)              // OrderAggregator.Api
        .Build();

    // Each project mapped to its representative assembly. ArchUnitNET treats
    // the assembly identity as the "module" boundary.
    private static readonly IObjectProvider<IType> ModelsLayer =
        Types().That().ResideInAssembly(typeof(Order).Assembly).As("Models");
    private static readonly IObjectProvider<IType> AbstractionsLayer =
        Types().That().ResideInAssembly(typeof(IProductRepository).Assembly).As("Abstractions");
    private static readonly IObjectProvider<IType> SharedLayer =
        Types().That().ResideInAssembly(typeof(AggregationOptions).Assembly).As("Shared");
    private static readonly IObjectProvider<IType> ServicesLayer =
        Types().That().ResideInAssembly(typeof(InMemoryOrderStore).Assembly).As("Services");
    private static readonly IObjectProvider<IType> ContractsLayer =
        Types().That().ResideInAssembly(typeof(OrderRequest).Assembly).As("Contracts");
    private static readonly IObjectProvider<IType> ResourcesLayer =
        Types().That().ResideInAssembly(typeof(ApiMessages).Assembly).As("Resources");
    private static readonly IObjectProvider<IType> ApiLayer =
        Types().That().ResideInAssembly(typeof(Program).Assembly).As("Api");

    /// <summary>
    /// The forbidden set for a leaf assembly: every other loaded project, derived
    /// as "all types in the architecture that are NOT in this assembly". Expressing
    /// the complement this way (instead of hand-listing each layer) keeps the rule
    /// correct as the solution grows — a project added to the loader above is
    /// covered automatically, with nothing to remember to extend here.
    /// </summary>
    /// <remarks>
    /// Do NOT replace this with the parameterless <c>NotDependOnAny()</c> overload:
    /// that one asserts independence from an empty set and therefore always passes —
    /// a silent no-op that gives a leaf rule no teeth. The complement must be passed
    /// explicitly.
    /// </remarks>
    private static IObjectProvider<IType> EveryProjectExcept(System.Reflection.Assembly self, string layerName) =>
        Types().That().DoNotResideInAssembly(self).As($"any project other than {layerName}");

    [Fact]
    public void Contracts_ShouldNotDependOnOtherProjectAssemblies()
    {
        // Arrange: the shared Architecture (loaded once, see the static field above).
        // Contracts must remain a leaf assembly so it can be packaged as a
        // client SDK without dragging in domain / hosting code.

        // Act & Assert (the fluent rule both expresses and verifies the invariant via Check)
        Types().That().Are(ContractsLayer)
            .Should().NotDependOnAny(EveryProjectExcept(typeof(OrderRequest).Assembly, "Contracts"))
            .Because("Contracts must stay leaf so it can be shipped to clients without hidden transitive deps.")
            .Check(Architecture);
    }

    [Fact]
    public void Models_ShouldNotDependOnOtherProjectAssemblies()
    {
        // Arrange: the shared Architecture (loaded once, see the static field above).
        // Models is pure domain — must not be polluted with ASP.NET, Mapster,
        // EF, etc. via accidental cross-project references.

        // Act & Assert
        Types().That().Are(ModelsLayer)
            .Should().NotDependOnAny(EveryProjectExcept(typeof(Order).Assembly, "Models"))
            .Because("Models is the domain — keep it free of infrastructure and wire concerns.")
            .Check(Architecture);
    }

    [Fact]
    public void Resources_ShouldNotDependOnOtherProjectAssemblies()
    {
        // Arrange: the shared Architecture (loaded once, see the static field above).
        // Localized strings are a leaf concern — the generated ApiMessages class
        // wraps only BCL ResourceManager. Keeping Resources leaf means any layer
        // (Api today, Services tomorrow) can reference it without forming a cycle.

        // Act & Assert
        Types().That().Are(ResourcesLayer)
            .Should().NotDependOnAny(EveryProjectExcept(typeof(ApiMessages).Assembly, "Resources"))
            .Because("Resources holds only localized strings; it must stay leaf so any layer can consume it.")
            .Check(Architecture);
    }

    [Fact]
    public void Abstractions_ShouldNotDependOnImplementationsOrApi()
    {
        // Act & Assert (Arrange = the shared Architecture loaded in the static field above)
        Types().That().Are(AbstractionsLayer)
            .Should().NotDependOnAny(ServicesLayer)
            .AndShould().NotDependOnAny(ApiLayer)
            .Because("Interfaces define the seam; they must not know their implementations or the composition root.")
            .Check(Architecture);
    }

    [Fact]
    public void Shared_ShouldNotDependOnImplementationsOrApi()
    {
        // Act & Assert (Arrange = the shared Architecture loaded in the static field above)
        Types().That().Are(SharedLayer)
            .Should().NotDependOnAny(ServicesLayer)
            .AndShould().NotDependOnAny(ApiLayer)
            .Because("Shared config/constant types are referenced from everywhere; pulling Api/Services from Shared would form a cycle.")
            .Check(Architecture);
    }

    [Fact]
    public void Services_ShouldNotDependOnApi()
    {
        // Act & Assert (Arrange = the shared Architecture loaded in the static field above)
        Types().That().Are(ServicesLayer)
            .Should().NotDependOnAny(ApiLayer)
            .Because("The implementation layer must not know about the ASP.NET hosting layer above it.")
            .Check(Architecture);
    }

    [Fact]
    public void DtoTypes_InContracts_ShouldBeNamed_DtoOrRequestOrResponse()
    {
        // Arrange: the shared Architecture (loaded once, see the static field above).
        // Naming convention keeps Contracts small and intentional: anything
        // added here is obviously a wire type, never a helper or a service.

        // Act & Assert
        Classes().That().Are(ContractsLayer)
            .Should().HaveNameEndingWith("Dto")
            .OrShould().HaveNameEndingWith("Request")
            .OrShould().HaveNameEndingWith("Response")
            .Because("Contracts holds only wire types — naming convention prevents random helpers from sneaking in.")
            .Check(Architecture);
    }
}
