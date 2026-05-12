using System.Security;

namespace MEL.Spectre.Rendering;

internal static class TtyDetector
{
    public static bool IsInteractiveTty()
    {
        try
        {
            return !System.Console.IsOutputRedirected && !System.Console.IsErrorRedirected;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or SecurityException)
        {
            return false;
        }
    }
}
