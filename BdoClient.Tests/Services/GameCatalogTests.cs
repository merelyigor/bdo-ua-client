using BdoClient.Services;

namespace BdoClient.Tests.Services;

public sealed class GameCatalogTests
{
    [Fact]
    public void ProductionCatalog_ContainsOnlyBlackDesert()
    {
        var catalog = GameCatalog.Create(BdoGameDefinition.Default);

        var game = Assert.Single(catalog.Games);
        Assert.Equal("black-desert-online", game.Id);
        Assert.Equal("Black Desert Online", game.DisplayName);
    }

    [Fact]
    public void Resolve_KnownId_ReturnsRegisteredGame()
    {
        var catalog = new GameCatalog(new[]
        {
            new GameDescriptor("black-desert-online", "Black Desert Online"),
            new GameDescriptor("synthetic-game", "Synthetic Game")
        });

        Assert.Equal("synthetic-game", catalog.Resolve("SYNTHETIC-GAME").Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("removed-game")]
    public void Resolve_MissingOrUnknownId_FallsBackToDefault(string? id)
    {
        var catalog = GameCatalog.Create(BdoGameDefinition.Default);

        Assert.Equal(catalog.DefaultGame, catalog.Resolve(id));
    }

    [Fact]
    public void Constructor_RejectsDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => new GameCatalog(new[]
        {
            new GameDescriptor("duplicate", "One"),
            new GameDescriptor("DUPLICATE", "Two")
        }));
    }
}
