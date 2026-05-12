using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Spectre.MEL.Theme;

public sealed class SpectreTheme
{
    private readonly Dictionary<LogLevel, Style> _levels;

    public SpectreTheme()
    {
        _levels = new Dictionary<LogLevel, Style>
        {
            [LogLevel.Trace] = new(Color.Grey50, decoration: Decoration.Dim),
            [LogLevel.Debug] = new(Color.Grey70),
            [LogLevel.Information] = new(Color.Green),
            [LogLevel.Warning] = new(Color.Gold1),
            [LogLevel.Error] = new(Color.Red1),
            [LogLevel.Critical] = new(Color.White, Color.Red, Decoration.Bold),
        };
    }

    public Style TimestampStyle { get; set; } = new(Color.Grey50);
    public Style CategoryStyle { get; set; } = new(Color.Grey70);
    public Style MessageStyle { get; set; } = new(Color.Default);
    public Style ExceptionStyle { get; set; } = new(Color.Red);
    public Style ScopeStyle { get; set; } = new(Color.Grey, decoration: Decoration.Italic);
    public Style EventIdStyle { get; set; } = new(Color.Grey50, decoration: Decoration.Dim);

    public PlaceholderStyleResolver Placeholders { get; private set; } = BuildDefaultPlaceholders();

    public Style ForLevel(LogLevel level) => _levels.TryGetValue(level, out var s) ? s : new Style();

    public SpectreTheme ForLevel(LogLevel level, Style style)
    {
        _levels[level] = style;
        return this;
    }

    public SpectreTheme ForLevel(LogLevel level, Color color) => ForLevel(level, new Style(color));

    public SpectreTheme WithPlaceholders(Action<PlaceholderStyleResolver> configure)
    {
        configure(Placeholders);
        return this;
    }

    public static SpectreTheme Default => new();

    public static SpectreTheme Dark => new SpectreTheme
    {
        TimestampStyle = new Style(Color.Grey39),
        CategoryStyle = new Style(Color.MediumPurple3),
        MessageStyle = new Style(Color.Grey85),
        ScopeStyle = new Style(Color.Grey46, decoration: Decoration.Italic),
        EventIdStyle = new Style(Color.Grey39, decoration: Decoration.Dim),
    }.ForLevel(LogLevel.Trace, new Style(Color.Grey39, decoration: Decoration.Dim))
     .ForLevel(LogLevel.Debug, new Style(Color.DodgerBlue1))
     .ForLevel(LogLevel.Information, new Style(Color.SpringGreen2))
     .ForLevel(LogLevel.Warning, new Style(Color.Yellow))
     .ForLevel(LogLevel.Error, new Style(Color.Red1))
     .ForLevel(LogLevel.Critical, new Style(Color.White, Color.DarkRed, Decoration.Bold));

    public static SpectreTheme Light => new SpectreTheme
    {
        TimestampStyle = new Style(Color.Grey50),
        CategoryStyle = new Style(Color.Purple),
        MessageStyle = new Style(Color.Black),
        ScopeStyle = new Style(Color.Grey39, decoration: Decoration.Italic),
        EventIdStyle = new Style(Color.Grey50, decoration: Decoration.Dim),
    }.ForLevel(LogLevel.Trace, new Style(Color.Grey50, decoration: Decoration.Dim))
     .ForLevel(LogLevel.Debug, new Style(Color.Blue))
     .ForLevel(LogLevel.Information, new Style(Color.DarkGreen))
     .ForLevel(LogLevel.Warning, new Style(Color.DarkOrange3))
     .ForLevel(LogLevel.Error, new Style(Color.Red3))
     .ForLevel(LogLevel.Critical, new Style(Color.White, Color.Red3_1, Decoration.Bold));

    public static SpectreTheme Monochrome
    {
        get
        {
            var theme = new SpectreTheme
            {
                TimestampStyle = Style.Plain,
                CategoryStyle = Style.Plain,
                MessageStyle = Style.Plain,
                ExceptionStyle = Style.Plain,
                ScopeStyle = Style.Plain,
                EventIdStyle = Style.Plain,
                Placeholders = new PlaceholderStyleResolver
                {
                    DefaultStyle = Style.Plain,
                    NullStyle = Style.Plain,
                },
            };
            foreach (var level in Enum.GetValues<LogLevel>())
            {
                if (level != LogLevel.None)
                {
                    theme.ForLevel(level, Style.Plain);
                }
            }
            return theme;
        }
    }

    private static PlaceholderStyleResolver BuildDefaultPlaceholders()
    {
        var resolver = new PlaceholderStyleResolver
        {
            DefaultStyle = new Style(Color.Grey85),
            NullStyle = new Style(Color.Grey50, decoration: Decoration.Dim),
        };

        resolver.ForType<int>(Color.Cyan1);
        resolver.ForType<long>(Color.Cyan1);
        resolver.ForType<short>(Color.Cyan1);
        resolver.ForType<byte>(Color.Cyan1);
        resolver.ForType<uint>(Color.Cyan1);
        resolver.ForType<ulong>(Color.Cyan1);
        resolver.ForType<ushort>(Color.Cyan1);
        resolver.ForType<sbyte>(Color.Cyan1);
        resolver.ForType<float>(Color.Aqua);
        resolver.ForType<double>(Color.Aqua);
        resolver.ForType<decimal>(Color.Aqua);
        resolver.ForType<bool>(Color.Magenta1);
        resolver.ForType<string>(Color.Yellow);
        resolver.ForType<DateTime>(Color.MediumPurple1);
        resolver.ForType<DateTimeOffset>(Color.MediumPurple1);
        resolver.ForType<DateOnly>(Color.MediumPurple1);
        resolver.ForType<TimeOnly>(Color.MediumPurple1);
        resolver.ForType<TimeSpan>(Color.MediumPurple1);
        resolver.ForType<Guid>(Color.SpringGreen3);
        resolver.ForType<Uri>(Color.DodgerBlue1);
#pragma warning disable CA2263
        resolver.ForType(typeof(Enum), new Style(Color.HotPink));
#pragma warning restore CA2263

        resolver.ForNamePattern("(?i)^(user|account|customer)?id$", new Style(Color.Aqua, decoration: Decoration.Bold));
        resolver.ForNamePattern("(?i)email", new Style(Color.DodgerBlue1));
        resolver.ForNamePattern("(?i)(path|file|directory|folder)", new Style(Color.PaleGreen3));
        resolver.ForNamePattern("(?i)(url|uri|endpoint)", new Style(Color.DodgerBlue1));
        resolver.ForNamePattern("(?i)(statuscode|httpstatus)", new Style(Color.Gold1, decoration: Decoration.Bold));
        resolver.ForNamePattern("(?i)(duration|elapsed|latency|ms)$", new Style(Color.Yellow));
        resolver.ForNamePattern("(?i)(count|total|size|length)$", new Style(Color.Cyan1));
        resolver.ForNamePattern("(?i)(host|machine|node|server)", new Style(Color.PaleTurquoise1));

        return resolver;
    }
}
