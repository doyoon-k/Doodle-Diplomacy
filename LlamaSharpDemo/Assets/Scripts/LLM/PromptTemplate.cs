using System.Text.RegularExpressions;

public class PromptTemplate
{
    private static readonly Regex PlaceholderRegex = new Regex(
        "{{([A-Za-z0-9_]+)}}",
        RegexOptions.Compiled);

    private readonly string _template;

    public PromptTemplate(string template)
    {
        _template = template ?? string.Empty;
    }

    public string Render(PipelineState state)
    {
        if (string.IsNullOrEmpty(_template) || state == null)
        {
            return _template;
        }

        return PlaceholderRegex.Replace(_template, match =>
        {
            if (match.Groups.Count < 2)
            {
                return match.Value;
            }

            return state.GetTemplateValue(match.Groups[1].Value);
        });
    }
}
