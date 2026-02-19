using System.Collections.Generic;
using System.Text;

public class PromptTemplate
{
    private readonly string template;
    public PromptTemplate(string template) => this.template = template;

    public string Render(Dictionary<string, string> state)
    {
        var sb = new StringBuilder(template);
        foreach (var kv in state)
        {
            sb.Replace($"{{{{{kv.Key}}}}}", kv.Value);
        }
        return sb.ToString();
    }
}
