using System;
using System.Collections;
using System.Collections.Generic;

public class StateSequentialChainExecutor
{
    private readonly List<IStateChainLink> _links = new();

    public void AddLink(IStateChainLink link) => _links.Add(link);

    // 호출부에서 StartCoroutine으로 돌리기
    public IEnumerator Execute(
        Dictionary<string, string> initialState,
        Action<Dictionary<string, string>> onComplete
    )
    {
        var state = initialState;
        foreach (var link in _links)
        {
            Dictionary<string, string> next = null;
            yield return link.Execute(state, s => next = s);
            state = next;
        }
        onComplete(state);
    }
}
