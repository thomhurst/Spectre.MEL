using System.Diagnostics;

namespace MEL.Spectre.Provider;

internal static class LogWriterDiagnostics
{
    public static void Emit(string message)
    {
        var written = false;
        try
        {
            System.Console.Error.WriteLine(message);
            written = true;
        }
        catch
        {
        }

        if (!written)
        {
            try
            {
                Debug.WriteLine(message);
            }
            catch
            {
            }
        }
    }
}
