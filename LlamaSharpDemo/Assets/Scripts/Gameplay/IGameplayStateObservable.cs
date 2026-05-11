using System;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Gameplay
{
    public interface IGameplayStateObservable
    {
        GameState CurrentState { get; }
        event Action<GameState> StateChanged;
    }
}