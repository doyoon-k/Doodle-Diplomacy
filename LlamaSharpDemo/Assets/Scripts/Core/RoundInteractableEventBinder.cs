using System;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundInteractableEventBinder
    {
        private readonly InteractableObject[] _alienInteractables;
        private readonly InteractableObject[] _tabletInteractables;
        private readonly InteractableObject _sharedMonitorInteractable;
        private readonly InteractableObject[] _terminalInteractables;
        private readonly UnityEngine.Object _logContext;

        public RoundInteractableEventBinder(
            InteractableObject[] alienInteractables,
            InteractableObject[] tabletInteractables,
            InteractableObject sharedMonitorInteractable,
            InteractableObject[] terminalInteractables,
            UnityEngine.Object logContext)
        {
            _alienInteractables = alienInteractables;
            _tabletInteractables = tabletInteractables;
            _sharedMonitorInteractable = sharedMonitorInteractable;
            _terminalInteractables = terminalInteractables;
            _logContext = logContext;
        }

        public void Bind(
            Action onAlienClicked,
            Action onTabletClicked,
            Action onTerminalClicked,
            Action onSharedMonitorClicked)
        {
            BindExplicit(_alienInteractables, InteractionType.Alien, onAlienClicked);
            BindExplicit(_tabletInteractables, InteractionType.Tablet, onTabletClicked);
            BindSharedMonitor(onTabletClicked, onSharedMonitorClicked);
            BindTerminals(onTabletClicked, onTerminalClicked);
        }

        public void Unbind(
            Action onAlienClicked,
            Action onTabletClicked,
            Action onTerminalClicked,
            Action onSharedMonitorClicked)
        {
            UnbindExplicit(_alienInteractables, onAlienClicked);
            UnbindExplicit(_tabletInteractables, onTabletClicked);
            UnbindSharedMonitor(onSharedMonitorClicked);
            UnbindTerminals(onTerminalClicked);
        }

        private void BindSharedMonitor(Action legacyTabletHandler, Action monitorHandler)
        {
            if (_sharedMonitorInteractable == null)
            {
                Debug.LogWarning("[RoundInteractableEventBinder] sharedMonitorInteractable is not assigned. Monitor click zoom will be disabled.", _logContext);
                return;
            }

            _sharedMonitorInteractable.interactionType = InteractionType.Monitor;
            RemoveListener(_sharedMonitorInteractable, legacyTabletHandler);
            RemoveListener(_sharedMonitorInteractable, monitorHandler);
            AddListener(_sharedMonitorInteractable, monitorHandler);
        }

        private void UnbindSharedMonitor(Action monitorHandler)
        {
            if (_sharedMonitorInteractable == null)
            {
                return;
            }

            RemoveListener(_sharedMonitorInteractable, monitorHandler);
        }

        private void BindTerminals(Action legacyTabletHandler, Action terminalHandler)
        {
            if (_terminalInteractables == null || _terminalInteractables.Length == 0)
            {
                Debug.LogWarning("[RoundInteractableEventBinder] terminalInteractables is empty. Terminal click interaction must be wired in inspector.", _logContext);
                return;
            }

            for (int i = 0; i < _terminalInteractables.Length; i++)
            {
                InteractableObject terminalInteractable = _terminalInteractables[i];
                if (terminalInteractable == null)
                {
                    continue;
                }

                terminalInteractable.interactionType = InteractionType.Terminal;
                RemoveListener(terminalInteractable, legacyTabletHandler);
                RemoveListener(terminalInteractable, terminalHandler);
                AddListener(terminalInteractable, terminalHandler);
            }
        }

        private void UnbindTerminals(Action terminalHandler)
        {
            if (_terminalInteractables == null)
            {
                return;
            }

            for (int i = 0; i < _terminalInteractables.Length; i++)
            {
                if (_terminalInteractables[i] == null)
                {
                    continue;
                }

                RemoveListener(_terminalInteractables[i], terminalHandler);
            }
        }

        private static void BindExplicit(InteractableObject[] interactables, InteractionType interactionType, Action handler)
        {
            if (interactables == null)
            {
                return;
            }

            for (int i = 0; i < interactables.Length; i++)
            {
                InteractableObject interactable = interactables[i];
                if (interactable == null)
                {
                    continue;
                }

                interactable.interactionType = interactionType;
                RemoveListener(interactable, handler);
                AddListener(interactable, handler);
            }
        }

        private static void UnbindExplicit(InteractableObject[] interactables, Action handler)
        {
            if (interactables == null)
            {
                return;
            }

            for (int i = 0; i < interactables.Length; i++)
            {
                InteractableObject interactable = interactables[i];
                if (interactable == null)
                {
                    continue;
                }

                RemoveListener(interactable, handler);
            }
        }

        private static void AddListener(InteractableObject interactable, Action handler)
        {
            if (interactable == null || handler == null)
            {
                return;
            }

            interactable.OnInteracted.AddListener(handler.Invoke);
        }

        private static void RemoveListener(InteractableObject interactable, Action handler)
        {
            if (interactable == null || handler == null)
            {
                return;
            }

            interactable.OnInteracted.RemoveListener(handler.Invoke);
        }
    }
}
