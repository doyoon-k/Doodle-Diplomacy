using System;
using System.Collections;

public interface IStateChainLink
{
    IEnumerator Execute(
        PipelineState state,
        Action<PipelineState> onDone
    );
}
