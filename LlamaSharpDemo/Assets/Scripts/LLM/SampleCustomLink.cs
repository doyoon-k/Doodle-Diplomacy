using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Example custom link for testing: writes a greeting into state["greeting"].
/// Shows how constructor parameters (string/int) are auto-bound from Custom Parameters.
/// </summary>
public class SampleCustomLink : IStateChainLink, ICustomLinkStateProvider
{
    private readonly string _name;
    private readonly string _title;
    private readonly int _level;

    // Parameterless fallback for safety.
    public SampleCustomLink() : this("Traveler", "Agent", 1) { }

    // This constructor is auto-detected; Custom Parameters (name/title/level) bind by parameter name.
    public SampleCustomLink(string name, string title, int level)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "Traveler" : name;
        _title = string.IsNullOrWhiteSpace(title) ? "Agent" : title;
        _level = level <= 0 ? 1 : level;
    }

    public IEnumerator Execute(
        Dictionary<string, string> state,
        Action<Dictionary<string, string>> onDone)
    {
        state ??= new Dictionary<string, string>();
        state["greeting"] = $"Hello {_title} {_name} (Lv {_level}), welcome aboard.";
        onDone?.Invoke(state);
        yield break;
    }

    public IEnumerable<string> GetWrites()
    {
        yield return "greeting";
    }
}
