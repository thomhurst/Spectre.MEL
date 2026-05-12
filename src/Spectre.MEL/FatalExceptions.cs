namespace Spectre.MEL;

internal static class FatalExceptions
{
    public static bool IsFatal(Exception ex) =>
        ex is OutOfMemoryException
        or StackOverflowException
        or AccessViolationException
        or ThreadAbortException;
}
