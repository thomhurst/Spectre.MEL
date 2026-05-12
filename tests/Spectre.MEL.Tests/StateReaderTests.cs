using Spectre.MEL.Provider;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class StateReaderTests
{
    [Test]
    public async Task Extracts_original_format_and_named_placeholders()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("UserId", 42),
            new("Email", "a@b.com"),
            new("{OriginalFormat}", "User {UserId} email {Email}"),
        };

        var (originalFormat, placeholders) = StateReader.Extract(state);

        await Assert.That(originalFormat).IsEqualTo("User {UserId} email {Email}");
        await Assert.That(placeholders).HasCount(2);
        await Assert.That(placeholders[0].Name).IsEqualTo("UserId");
        await Assert.That(placeholders[0].Value).IsEqualTo(42);
        await Assert.That(placeholders[1].Name).IsEqualTo("Email");
    }

    [Test]
    public async Task Returns_empty_for_state_without_format()
    {
        var (originalFormat, placeholders) = StateReader.Extract("just a string");
        await Assert.That(originalFormat).IsNull();
        await Assert.That(placeholders).IsEmpty();
    }

    [Test]
    public async Task Captures_runtime_value_types()
    {
        var state = new List<KeyValuePair<string, object?>>
        {
            new("Count", 7),
            new("Rate", 0.5),
            new("{OriginalFormat}", "x"),
        };

        var (_, placeholders) = StateReader.Extract(state);
        await Assert.That(placeholders[0].ValueType).IsEqualTo(typeof(int));
        await Assert.That(placeholders[1].ValueType).IsEqualTo(typeof(double));
    }
}
