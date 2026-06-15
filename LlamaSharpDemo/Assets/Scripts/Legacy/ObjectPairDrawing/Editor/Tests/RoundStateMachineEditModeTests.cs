using NUnit.Framework;

namespace DoodleDiplomacy.Core.Editor.Tests
{
    public sealed class RoundStateMachineEditModeTests
    {
        [Test]
        public void NewMachineStartsAtInitialStateWithZeroVersion()
        {
            var machine = new RoundStateMachine(GameState.Title);

            Assert.AreEqual(GameState.Title, machine.CurrentState);
            Assert.AreEqual(0, machine.StateVersion);
            Assert.IsTrue(machine.IsCurrent(GameState.Title, 0));
        }

        [Test]
        public void MoveToChangesStateAndIncrementsVersion()
        {
            var machine = new RoundStateMachine(GameState.Title);

            RoundStateTransition transition = machine.MoveTo(GameState.Intro);

            Assert.AreEqual(GameState.Title, transition.OldState);
            Assert.AreEqual(GameState.Intro, transition.NewState);
            Assert.AreEqual(1, transition.Version);
            Assert.AreEqual(GameState.Intro, machine.CurrentState);
            Assert.IsTrue(machine.IsCurrent(GameState.Intro, 1));
        }

        [Test]
        public void IsCurrentRejectsStaleVersion()
        {
            var machine = new RoundStateMachine(GameState.Title);
            machine.MoveTo(GameState.Intro);
            machine.MoveTo(GameState.WaitingForRound);

            Assert.IsFalse(machine.IsCurrent(GameState.Intro, 1));
            Assert.IsTrue(machine.IsCurrent(GameState.WaitingForRound, 2));
        }

        [Test]
        public void CanChangeToRejectsCurrentState()
        {
            var machine = new RoundStateMachine(GameState.Drawing);

            Assert.IsFalse(machine.CanChangeTo(GameState.Drawing));
            Assert.IsTrue(machine.CanChangeTo(GameState.PreviewReady));
        }
    }
}