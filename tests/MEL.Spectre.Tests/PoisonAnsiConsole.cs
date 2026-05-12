using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace MEL.Spectre.Tests;

internal sealed class PoisonAnsiConsole : IAnsiConsole
{
    private readonly TestConsole _inner = new();
    private readonly string _poisonNeedle;

    public PoisonAnsiConsole(string poisonNeedle)
    {
        _inner.Profile.Width = 1_000_000;
        _poisonNeedle = poisonNeedle;
    }

    public string Output => _inner.Output;

    public Profile Profile => _inner.Profile;
    public IAnsiConsoleCursor Cursor => _inner.Cursor;
    public IAnsiConsoleInput Input => _inner.Input;
    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;
    public RenderPipeline Pipeline => _inner.Pipeline;

    public void Clear(bool home) => _inner.Clear(home);
    public void Write(IRenderable renderable)
    {
        var snapshotBefore = _inner.Output.Length;
        _inner.Write(renderable);
        var written = _inner.Output[snapshotBefore..];
        if (written.Contains(_poisonNeedle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Poisoned entry");
        }
    }
    public void WriteAnsi(Action<AnsiWriter> action) => _inner.WriteAnsi(action);
}
