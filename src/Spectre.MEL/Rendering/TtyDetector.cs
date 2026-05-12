namespace Spectre.MEL.Rendering;

internal static class TtyDetector
{
    public static bool IsInteractiveTty()
    {
        try
        {
            return !System.Console.IsOutputRedirected && !System.Console.IsErrorRedirected;
        }
        catch
        {
            return false;
        }
    }
}
