namespace MEL.Spectre;

/// <summary>
/// Handling of raw ANSI/VT control sequences embedded in log message content — typically stdout/stderr
/// relayed from child processes (dotnet, npm, ...) that styled their output for a terminal. An embedded
/// reset (<c>ESC[0m</c>) would otherwise terminate the logger's own styling mid-line.
/// </summary>
public enum EmbeddedAnsiMode
{
    /// <summary>
    /// Translate embedded SGR (color/style) sequences into Spectre markup. The child process's colors are
    /// preserved and properly nested: an embedded reset closes only the embedded style and the theme's
    /// outer style resumes afterwards. All other control sequences (cursor movement, screen clearing,
    /// OSC titles/hyperlinks, ...) and stray control characters are removed. This is the default.
    /// </summary>
    Convert,

    /// <summary>
    /// Remove all embedded ANSI/VT control sequences and stray control characters from message content,
    /// discarding the child process's styling.
    /// </summary>
    Strip,

    /// <summary>
    /// Leave message content untouched. Embedded sequences reach the terminal raw and may corrupt the
    /// outer styling or CI annotation payloads.
    /// </summary>
    Passthrough,
}
