namespace MEL.Spectre.Templates;

internal sealed class OutputTemplate
{
    public OutputTemplate(string template)
    {
        Template = template;
        Segments = TemplateParser.Parse(template);
    }

    public string Template { get; }

    public TemplateSegment[] Segments { get; }
}
