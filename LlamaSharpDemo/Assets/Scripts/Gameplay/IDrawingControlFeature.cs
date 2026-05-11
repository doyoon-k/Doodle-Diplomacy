using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public interface IDrawingControlFeature : IDrawingFeature
    {
        event Action<int> BrushRadiusChanged;
        event Action<bool, bool> HistoryStateChanged;

        int BrushRadius { get; }
        Color BrushColor { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
    }
}
