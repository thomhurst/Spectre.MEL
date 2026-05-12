using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace Spectre.MEL.Tests;

internal sealed class BlockingAnsiConsole : IAnsiConsole
{
    private readonly TestConsole _inner = new();
    private readonly ManualResetEventSlim _gate = new(initialState: false);

    public BlockingAnsiConsole()
    {
        _inner.Profile.Width = 1_000_000;
    }

    public string Output => _inner.Output;

    public void Release() => _gate.Set();

    public Profile Profile => _inner.Profile;
    public IAnsiConsoleCursor Cursor => _inner.Cursor;
    public IAnsiConsoleInput Input => _inner.Input;
    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;
    public RenderPipeline Pipeline => _inner.Pipeline;

    public void Clear(bool home) => _inner.Clear(home);
    public void Write(IRenderable renderable)
    {
        _gate.Wait();
        _inner.Write(renderable);
    }
    public void WriteAnsi(Action<AnsiWriter> action)
    {
        _gate.Wait();
        _inner.WriteAnsi(action);
    }
}
