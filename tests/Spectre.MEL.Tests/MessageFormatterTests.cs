using Spectre.MEL.Masking;
using Spectre.MEL.Provider;
using Spectre.MEL.Rendering;
using Spectre.MEL.Theme;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class MessageFormatterTests
{
    private static SecretMasker NewMasker() => new(new SpectreConsoleLoggerOptions().MaskedNamePatterns, 256);

    [Test]
    public async Task Substitutes_named_placeholders()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[]
        {
            new Placeholder("UserId", 42, typeof(int)),
            new Placeholder("Email", "a@b", typeof(string)),
        };

        var result = MessageFormatter.Render("User {UserId} email {Email}", "fallback", placeholders, theme, masker);
        await Assert.That(result).IsEqualTo("User 42 email a@b");
    }

    [Test]
    public async Task Masks_secret_named_placeholder()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var placeholders = new[]
        {
            new Placeholder("Authorization", "Bearer xyz", typeof(string)),
        };

        var collected = new List<string>();
        var result = MessageFormatter.Render("Header {Authorization}", "fallback", placeholders, theme, masker, collected);
        await Assert.That(result).IsEqualTo("Header ***");
        await Assert.That(collected).Contains("Bearer xyz");
    }

    [Test]
    public async Task Escapes_markup_brackets_in_literal()
    {
        var theme = SpectreTheme.Monochrome;
        var masker = NewMasker();
        var result = MessageFormatter.Render("[group] {Value}", "fb", [new Placeholder("Value", 1, typeof(int))], theme, masker);
        await Assert.That(result).IsEqualTo("[[group]] 1");
    }
}
