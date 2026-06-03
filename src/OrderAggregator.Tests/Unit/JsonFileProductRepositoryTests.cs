using Microsoft.Extensions.Logging.Abstractions;
using OrderAggregator.Services.Products;

namespace OrderAggregator.Tests.Unit;

[Trait(TestCategories.Name, TestCategories.Unit)]
public class JsonFileProductRepositoryTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"products-{Guid.NewGuid():N}.json");

    [Fact]
    public void LoadFromFile_ReadsCatalog_AndExposesLookup()
    {
        // Arrange
        File.WriteAllText(_tempFile, """
            [
              { "ProductId": "1", "ProductName": "Alpha" },
              { "ProductId": "2", "ProductName": "Beta" }
            ]
            """);

        // Act
        var repo = JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance);

        // Assert
        Assert.Equal(2, repo.Count);
        Assert.True(repo.Exists("1"));
        Assert.True(repo.Exists("2"));
        Assert.False(repo.Exists("3"));
        Assert.Equal("Alpha", repo.Find("1")!.ProductName);
        Assert.Null(repo.Find("missing"));
    }

    [Fact]
    public void LoadFromFile_DedupesDuplicateIds_KeepingFirst()
    {
        // Arrange
        File.WriteAllText(_tempFile, """
            [
              { "ProductId": "1", "ProductName": "First" },
              { "ProductId": "1", "ProductName": "Second" }
            ]
            """);

        // Act
        var repo = JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance);

        // Assert
        Assert.Equal(1, repo.Count);
        Assert.Equal("First", repo.Find("1")!.ProductName);
    }

    [Fact]
    public void LoadFromFile_ThrowsWhenFileMissing()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => JsonFileProductRepository.LoadFromFile(path, NullLogger.Instance));
    }

    [Fact]
    public void LoadFromFile_ThrowsOnEmptyCatalog()
    {
        // Arrange
        File.WriteAllText(_tempFile, "[]");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance));
    }

    [Fact]
    public void Exists_ReturnsFalse_ForNullOrEmptyId()
    {
        // Arrange
        File.WriteAllText(_tempFile, """[ { "ProductId": "1", "ProductName": "Alpha" } ]""");
        var repo = JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance);

        // Act & Assert
        Assert.False(repo.Exists(""));
        Assert.False(repo.Exists(null!));
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }
}
