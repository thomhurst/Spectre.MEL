using Spectre.Console;
using Spectre.MEL.Masking;
using Spectre.MEL.Rendering;

namespace Spectre.MEL.Ci;

internal sealed record RendererContext(EntryFormatter Formatter, SecretMasker Masker, ExceptionFormats ExceptionFormats);
