using Spectre.MEL.Templates;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class TemplateParserTests
{
    [Test]
    public async Task Parses_literal_only_template()
    {
        var segments = TemplateParser.Parse("hello world");
        await Assert.That(segments).Count().IsEqualTo(1);
        await Assert.That(segments[0].Kind).IsEqualTo(SegmentKind.Literal);
        await Assert.That(segments[0].Literal).IsEqualTo("hello world");
    }

    [Test]
    public async Task Parses_known_tokens()
    {
        var segments = TemplateParser.Parse("[{Timestamp:HH:mm:ss} {Level:u4} {Category}] {Message}");

        await Assert.That(segments[1].Kind).IsEqualTo(SegmentKind.Timestamp);
        await Assert.That(segments[1].Format).IsEqualTo("HH:mm:ss");
        await Assert.That(segments[3].Kind).IsEqualTo(SegmentKind.Level);
        await Assert.That(segments[3].Format).IsEqualTo("u4");
        await Assert.That(segments[5].Kind).IsEqualTo(SegmentKind.Category);
        await Assert.That(segments[7].Kind).IsEqualTo(SegmentKind.Message);
    }

    [Test]
    public async Task Treats_unknown_tokens_as_custom()
    {
        var segments = TemplateParser.Parse("{UserId}");
        await Assert.That(segments[0].Kind).IsEqualTo(SegmentKind.Custom);
        await Assert.That(segments[0].Name).IsEqualTo("UserId");
    }

    [Test]
    public async Task Escapes_double_braces()
    {
        var segments = TemplateParser.Parse("{{not a token}}");
        await Assert.That(segments).Count().IsEqualTo(1);
        await Assert.That(segments[0].Literal).IsEqualTo("{not a token}");
    }

    [Test]
    public async Task Throws_on_unterminated_token()
    {
        await Assert.That(() => TemplateParser.Parse("{Level"))
            .Throws<FormatException>();
    }

    [Test]
    public async Task Throws_on_unmatched_close_brace()
    {
        await Assert.That(() => TemplateParser.Parse("hello}"))
            .Throws<FormatException>();
    }
}
