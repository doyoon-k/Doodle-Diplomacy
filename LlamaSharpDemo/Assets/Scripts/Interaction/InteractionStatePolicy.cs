using System.Collections.Generic;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Interaction
{
    public static class InteractionStatePolicy
    {
        private static readonly HashSet<InteractionType> WaitingAllowed = new() { InteractionType.Alien };
        private static readonly HashSet<InteractionType> ObjectPresentedAllowed = new()
        {
            InteractionType.Alien,
            InteractionType.Tablet,
            InteractionType.Monitor
        };
        private static readonly HashSet<InteractionType> DrawingAllowed = new() { InteractionType.Tablet };
        private static readonly HashSet<InteractionType> PreviewReadyAllowed = new()
        {
            InteractionType.Tablet,
            InteractionType.Alien,
            InteractionType.Monitor
        };
        private static readonly HashSet<InteractionType> PreviewAllowed = new() { InteractionType.Terminal };
        private static readonly HashSet<InteractionType> InterpreterReadyAllowed = new() { InteractionType.Alien, InteractionType.Terminal };
        private static readonly HashSet<InteractionType> InterpreterAllowed = new() { InteractionType.Terminal };
        private static readonly HashSet<InteractionType> NoneAllowed = new();

        public static HashSet<InteractionType> GetAllowedTypes(GameState state)
        {
            return state switch
            {
                GameState.WaitingForRound => WaitingAllowed,
                GameState.ObjectPresented => ObjectPresentedAllowed,
                GameState.Drawing => DrawingAllowed,
                GameState.PreviewReady => PreviewReadyAllowed,
                GameState.Preview => PreviewAllowed,
                GameState.InterpreterReady => InterpreterReadyAllowed,
                GameState.Interpreter => InterpreterAllowed,
                _ => NoneAllowed
            };
        }

        public static bool IsAllowed(InteractionStateContext context, InteractionType interactionType)
        {
            if (!GetAllowedTypes(context.State).Contains(interactionType))
            {
                return false;
            }

            if (context.State == GameState.WaitingForRound && interactionType == InteractionType.Alien)
            {
                return true;
            }

            if (context.State == GameState.InterpreterReady && interactionType == InteractionType.Alien)
            {
                return context.InterpreterInspectionCompleted;
            }

            return true;
        }
    }
}
