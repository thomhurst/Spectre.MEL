using Spectre.Console;
using Spectre.MEL.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class PlaceholderStyleResolverTests
{
    [Test]
    public async Task Resolve_freezes_resolver()
    {
        var resolver = new PlaceholderStyleResolver();
        resolver.ForName("X", new Style(Color.Red));
        resolver.Resolve("X", 1);

        await Assert.That(() => resolver.ForName("Y", new Style(Color.Blue)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ForNamePattern_throws_after_freeze()
    {
        var resolver = new PlaceholderStyleResolver();
        resolver.Resolve("anything", 42);

        await Assert.That(() => resolver.ForNamePattern("(?i)foo", new Style(Color.Blue)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ForType_throws_after_freeze()
    {
        var resolver = new PlaceholderStyleResolver();
        resolver.Resolve("anything", 42);

        await Assert.That(() => resolver.ForType<bool>(new Style(Color.Magenta1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Theme_freezes_setters_after_provider_construction()
    {
        var theme = new SpectreTheme();
        theme.Freeze();

        await Assert.That(() => theme.TimestampStyle = new Style(Color.Red))
            .Throws<InvalidOperationException>();
    }
}
