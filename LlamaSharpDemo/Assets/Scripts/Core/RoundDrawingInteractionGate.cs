using DoodleDiplomacy.Gameplay;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundDrawingInteractionGate
    {
        private readonly IDrawingFeature _drawing;

        public RoundDrawingInteractionGate(IDrawingFeature drawing)
        {
            _drawing = drawing;
        }

        public void Apply(GameState state)
        {
            if (_drawing == null)
            {
                return;
            }

            _drawing.EnsureRuntimeEnabled();
            _drawing.SetInteractionLocked(state != GameState.Drawing);
        }

        public void UnlockForEditing()
        {
            if (_drawing == null)
            {
                return;
            }

            _drawing.EnsureRuntimeEnabled();
            _drawing.SetInteractionLocked(false);
        }
    }
}
