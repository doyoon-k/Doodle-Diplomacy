using System;
using System.Collections;
using System.Collections.Generic;

public class StateSequentialChainExecutor
{
    private readonly List<IStateChainLink> _links = new();

    public void AddLink(IStateChainLink link) => _links.Add(link);

    public IEnumerator Execute(
        PipelineState initialState,
        Action<PipelineState> onComplete
    )
    {
        var state = initialState ?? new PipelineState();
        foreach (var link in _links)
        {
            PipelineState next = null;
            yield return link.Execute(state, s => next = s);
            state = next ?? state;

            if (state.TryGetString(PromptPipelineConstants.ErrorKey, out _))
            {
                break;
            }
        }

        onComplete(state);
    }
}
