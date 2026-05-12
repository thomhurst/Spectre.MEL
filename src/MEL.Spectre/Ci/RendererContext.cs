using Spectre.Console;
using MEL.Spectre.Masking;
using MEL.Spectre.Rendering;

namespace MEL.Spectre.Ci;

internal sealed record RendererContext(
    EntryFormatter Formatter,
    SecretMasker Masker,
    ExceptionFormats ExceptionFormats,
    bool SuppressInlineLevelOnCiAnnotation = false);
