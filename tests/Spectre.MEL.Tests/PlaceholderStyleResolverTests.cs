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

    [Test]
    public async Task Theme_freeze_propagates_to_placeholders()
    {
        var theme = new SpectreTheme();
        theme.Freeze();

        await Assert.That(() => theme.Placeholders.ForName("Y", new Style(Color.Blue)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Theme_ForLevel_with_style_throws_after_freeze()
    {
        var theme = new SpectreTheme();
        theme.Freeze();

        await Assert.That(() => theme.ForLevel(Microsoft.Extensions.Logging.LogLevel.Information, new Style(Color.Red)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Theme_WithPlaceholders_throws_after_freeze()
    {
        var theme = new SpectreTheme();
        theme.Freeze();

        await Assert.That(() => theme.WithPlaceholders(p => p.ForName("Z", Color.Red)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Resolver_default_and_null_style_setters_throw_after_freeze()
    {
        var resolver = new PlaceholderStyleResolver();
        resolver.Resolve("anything", 1);

        await Assert.That(() => resolver.DefaultStyle = new Style(Color.Red))
            .Throws<InvalidOperationException>();
        await Assert.That(() => resolver.NullStyle = new Style(Color.Red))
            .Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("Default")]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Monochrome")]
    public async Task Static_theme_factories_return_fresh_instances(string factoryName)
    {
        var prop = typeof(SpectreTheme).GetProperty(factoryName)!;
        var first = (SpectreTheme)prop.GetValue(null)!;
        var second = (SpectreTheme)prop.GetValue(null)!;
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
    }
}
