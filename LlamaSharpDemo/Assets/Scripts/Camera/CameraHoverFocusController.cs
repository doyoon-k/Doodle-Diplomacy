using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Camera
{
    public sealed class CameraHoverFocusController
    {
        private InteractableObject _hoverCandidate;
        private InteractableObject _activeFocus;
        private float _hoverCandidateElapsed;
        private float _focusAcquireDelay;

        public CameraHoverFocusController(float focusAcquireDelay)
        {
            SetFocusAcquireDelay(focusAcquireDelay);
        }

        public InteractableObject ActiveFocus => _activeFocus;

        public void SetFocusAcquireDelay(float focusAcquireDelay)
        {
            _focusAcquireDelay = Mathf.Max(0f, focusAcquireDelay);
        }

        public void Update(InteractableObject hoveredObject, float deltaTime)
        {
            if (hoveredObject == _activeFocus && hoveredObject != null)
            {
                _hoverCandidate = null;
                _hoverCandidateElapsed = 0f;
                return;
            }

            if (hoveredObject == null)
            {
                _hoverCandidate = null;
                _hoverCandidateElapsed = 0f;
                return;
            }

            if (_hoverCandidate != hoveredObject)
            {
                _hoverCandidate = hoveredObject;
                _hoverCandidateElapsed = 0f;
                return;
            }

            _hoverCandidateElapsed += Mathf.Max(0f, deltaTime);
            if (_hoverCandidateElapsed >= _focusAcquireDelay)
            {
                _activeFocus = hoveredObject;
            }
        }

        public void ClearActiveFocus()
        {
            _activeFocus = null;
        }

        public void Reset()
        {
            _hoverCandidate = null;
            _activeFocus = null;
            _hoverCandidateElapsed = 0f;
        }
    }
}
