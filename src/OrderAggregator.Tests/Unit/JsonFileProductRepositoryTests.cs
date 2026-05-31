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
        File.WriteAllText(_tempFile, """
            [
              { "ProductId": "1", "ProductName": "Alpha" },
              { "ProductId": "2", "ProductName": "Beta" }
            ]
            """);

        var repo = JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance);

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
        File.WriteAllText(_tempFile, """
            [
              { "ProductId": "1", "ProductName": "First" },
              { "ProductId": "1", "ProductName": "Second" }
            ]
            """);

        var repo = JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance);

        Assert.Equal(1, repo.Count);
        Assert.Equal("First", repo.Find("1")!.ProductName);
    }

    [Fact]
    public void LoadFromFile_ThrowsWhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
        Assert.Throws<FileNotFoundException>(() => JsonFileProductRepository.LoadFromFile(path, NullLogger.Instance));
    }

    [Fact]
    public void LoadFromFile_ThrowsOnEmptyCatalog()
    {
        File.WriteAllText(_tempFile, "[]");
        Assert.Throws<InvalidOperationException>(() => JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance));
    }

    [Fact]
    public void Exists_ReturnsFalse_ForNullOrEmptyId()
    {
        File.WriteAllText(_tempFile, """[ { "ProductId": "1", "ProductName": "Alpha" } ]""");
        var repo = JsonFileProductRepository.LoadFromFile(_tempFile, NullLogger.Instance);

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
