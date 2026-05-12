using Spectre.Console;
using Spectre.MEL.Provider;
using Spectre.MEL.Scopes;

namespace Spectre.MEL.Ci;

internal interface ICiRenderer
{
    string Name { get; }

    CiCapabilities Capabilities { get; }

    void EmitMask(IAnsiConsole console, string value);

    void OpenScope(IAnsiConsole console, ScopeFrame frame, int depth);

    void CloseScope(IAnsiConsole console, ScopeFrame frame, int depth);

    void RenderEntry(IAnsiConsole console, LogEntry entry, int scopeDepth);
}
