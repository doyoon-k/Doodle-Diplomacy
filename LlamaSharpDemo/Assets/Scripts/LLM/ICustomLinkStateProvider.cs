using System.Collections.Generic;

/// <summary>
/// Optional metadata provider so the editor can infer state keys a custom link writes.
/// Implement on your IStateChainLink if you want GraphView to visualize the keys it produces.
/// </summary>
public interface ICustomLinkStateProvider
{
    IEnumerable<string> GetWrites();
}
