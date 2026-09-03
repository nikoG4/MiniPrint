using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Tests;

public sealed class WindowsPrinterCatalogTests
{
    [Fact]
    public void CreateSlug_IsReadableStableAndAscii()
    {
        var first = WindowsPrinterCatalog.CreateSlug("Administración – HP LaserJet");
        var second = WindowsPrinterCatalog.CreateSlug("Administración – HP LaserJet");

        Assert.Equal(first, second);
        Assert.StartsWith("administracion-hp-laserjet-", first);
        Assert.All(first, character => Assert.True(char.IsAsciiLetterOrDigit(character) || character == '-'));
    }

    [Fact]
    public void CreateSlug_DistinguishesSimilarNames()
    {
        Assert.NotEqual(
            WindowsPrinterCatalog.CreateSlug("HP Office 1"),
            WindowsPrinterCatalog.CreateSlug("HP Office 2"));
    }
}
