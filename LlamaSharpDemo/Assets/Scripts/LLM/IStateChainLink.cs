using System.Collections.Generic;
using System;
using System.Collections;

public interface IStateChainLink
{
    // state를 받아 처리한 뒤, 병합된 state를 onDone으로 넘겨준다.
    IEnumerator Execute(
        Dictionary<string, string> state,
        Action<Dictionary<string, string>> onDone
    );
}
