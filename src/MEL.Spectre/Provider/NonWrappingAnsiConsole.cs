using Spectre.Console;
using Spectre.Console.Rendering;

namespace MEL.Spectre.Provider;

internal sealed class NonWrappingAnsiConsole : IAnsiConsole
{
    private const int UnwrappedWidth = int.MaxValue;
    private readonly IAnsiConsole _inner;

    public NonWrappingAnsiConsole(IAnsiConsole inner)
    {
        _inner = inner;
        Profile = new Profile(inner.Profile.Out, inner.Profile.Capabilities, inner.Profile.Encoding)
        {
            Height = inner.Profile.Height,
            Width = UnwrappedWidth,
        };
    }

    public Profile Profile { get; }

    public IAnsiConsoleCursor Cursor => _inner.Cursor;

    public IAnsiConsoleInput Input => _inner.Input;

    public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

    public RenderPipeline Pipeline => _inner.Pipeline;

    public void Clear(bool home) => _inner.Clear(home);

    public void Write(IRenderable renderable)
    {
        var output = this.ToAnsi(renderable);
        _inner.WriteAnsi(writer => writer.Write(output));
    }

    public void WriteAnsi(Action<AnsiWriter> action) => _inner.WriteAnsi(action);
}
