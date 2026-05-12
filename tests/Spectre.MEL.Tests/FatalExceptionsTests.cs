using Spectre.MEL;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;

namespace Spectre.MEL.Tests;

public class FatalExceptionsTests
{
    [Test]
    public async Task OutOfMemoryException_is_fatal()
    {
        await Assert.That(FatalExceptions.IsFatal(new OutOfMemoryException())).IsTrue();
    }

    [Test]
    public async Task AccessViolationException_is_fatal()
    {
        await Assert.That(FatalExceptions.IsFatal(new AccessViolationException())).IsTrue();
    }

    [Test]
    [Arguments(typeof(InvalidOperationException))]
    [Arguments(typeof(ArgumentException))]
    [Arguments(typeof(FormatException))]
    [Arguments(typeof(NullReferenceException))]
    public async Task Common_exceptions_are_not_fatal(Type exceptionType)
    {
        var ex = (Exception)Activator.CreateInstance(exceptionType)!;
        await Assert.That(FatalExceptions.IsFatal(ex)).IsFalse();
    }
}
