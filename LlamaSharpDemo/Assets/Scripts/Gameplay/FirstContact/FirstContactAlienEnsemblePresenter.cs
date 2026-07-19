using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Character;
using DoodleDiplomacy.Core;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactAlienEnsemblePresenter
    {
        private const string PlaceholderTieName = "FC_TEMP_BlackTie";
        private const string PlaceholderDoorName = "FC_TEMP_DelegationDoor";

        private readonly List<AlienReactionController> _aliens = new();
        private readonly List<Vector3> _authoredPositions = new();
        private GameObject _placeholderDoor;
        private Material _blackMaterial;
        private Material _doorMaterial;
        private bool _positionsCaptured;

        public int Count => _aliens.Count;

        public FirstContactAlienEnsemblePresenter()
        {
            RefreshAliens();
        }

        public void RefreshAliens()
        {
            _aliens.Clear();
            AlienReactionController[] found = Object.FindObjectsByType<AlienReactionController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (found != null)
            {
                _aliens.AddRange(found);
            }

            _aliens.Sort((left, right) =>
                left.transform.position.x.CompareTo(right.transform.position.x));
            CaptureAuthoredPositions(force: true);
        }

        public void PreparePlaceholders(bool createGeometry)
        {
            if (!createGeometry)
            {
                return;
            }

            EnsurePlaceholderTies();
        }

        public IEnumerator PlayEntranceRoutine(
            float duration,
            float distance,
            bool createGeometry)
        {
            if (_aliens.Count == 0)
            {
                yield break;
            }

            CaptureAuthoredPositions(force: false);
            PreparePlaceholders(createGeometry);

            Vector3 center = GetCenter();
            Vector3 approachDirection = ResolveApproachDirection(center);
            float safeDistance = Mathf.Max(0f, distance);
            if (createGeometry)
            {
                EnsurePlaceholderDoor(center + approachDirection * safeDistance, approachDirection);
            }

            for (int i = 0; i < _aliens.Count; i++)
            {
                AlienReactionController alien = _aliens[i];
                if (alien == null)
                {
                    continue;
                }

                alien.transform.position = _authoredPositions[i] + approachDirection * safeDistance;
                alien.PlayAnimationOnly(SatisfactionLevel.Neutral, Mathf.Max(0.2f, duration), false);
            }

            float elapsed = 0f;
            float safeDuration = Mathf.Max(0.1f, duration);
            while (elapsed < safeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / safeDuration));
                for (int i = 0; i < _aliens.Count; i++)
                {
                    AlienReactionController alien = _aliens[i];
                    if (alien == null)
                    {
                        continue;
                    }

                    Vector3 start = _authoredPositions[i] + approachDirection * safeDistance;
                    alien.transform.position = Vector3.LerpUnclamped(start, _authoredPositions[i], t);
                }

                yield return null;
            }

            RestoreAuthoredPositions();
            if (_placeholderDoor != null)
            {
                Object.Destroy(_placeholderDoor);
                _placeholderDoor = null;
            }
        }

        public void PlayGroupReaction(SatisfactionLevel satisfaction, float duration)
        {
            float safeDuration = Mathf.Max(0.15f, duration);
            for (int i = 0; i < _aliens.Count; i++)
            {
                AlienReactionController alien = _aliens[i];
                if (alien == null)
                {
                    continue;
                }

                float staggeredDuration = safeDuration + i * 0.08f;
                alien.PlayAnimationOnly(satisfaction, staggeredDuration, true);
            }
        }

        public void RestoreAuthoredPositions()
        {
            if (!_positionsCaptured)
            {
                return;
            }

            int count = Mathf.Min(_aliens.Count, _authoredPositions.Count);
            for (int i = 0; i < count; i++)
            {
                if (_aliens[i] != null)
                {
                    _aliens[i].transform.position = _authoredPositions[i];
                }
            }
        }

        public void ClearPlaceholders()
        {
            if (_placeholderDoor != null)
            {
                Object.Destroy(_placeholderDoor);
                _placeholderDoor = null;
            }

            for (int i = 0; i < _aliens.Count; i++)
            {
                AlienReactionController alien = _aliens[i];
                Transform tie = alien != null ? alien.transform.Find(PlaceholderTieName) : null;
                if (tie != null)
                {
                    Object.Destroy(tie.gameObject);
                }
            }

            if (_blackMaterial != null)
            {
                Object.Destroy(_blackMaterial);
                _blackMaterial = null;
            }

            if (_doorMaterial != null)
            {
                Object.Destroy(_doorMaterial);
                _doorMaterial = null;
            }
        }

        public Vector3 GetCenter()
        {
            if (_aliens.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < _aliens.Count; i++)
            {
                if (_aliens[i] == null)
                {
                    continue;
                }

                sum += _aliens[i].transform.position;
                count++;
            }

            return count > 0 ? sum / count : Vector3.zero;
        }

        private void CaptureAuthoredPositions(bool force)
        {
            if (_positionsCaptured && !force)
            {
                return;
            }

            _authoredPositions.Clear();
            for (int i = 0; i < _aliens.Count; i++)
            {
                _authoredPositions.Add(_aliens[i] != null
                    ? _aliens[i].transform.position
                    : Vector3.zero);
            }

            _positionsCaptured = true;
        }

        private static Vector3 ResolveApproachDirection(Vector3 center)
        {
            UnityEngine.Camera camera = UnityEngine.Camera.main;
            if (camera == null)
            {
                return Vector3.back;
            }

            Vector3 fromCamera = center - camera.transform.position;
            fromCamera.y = 0f;
            return fromCamera.sqrMagnitude > 0.001f
                ? fromCamera.normalized
                : Vector3.back;
        }

        private void EnsurePlaceholderTies()
        {
            for (int i = 0; i < _aliens.Count; i++)
            {
                AlienReactionController alien = _aliens[i];
                if (alien == null || alien.transform.Find(PlaceholderTieName) != null)
                {
                    continue;
                }

                if (!TryGetWorldBounds(alien.gameObject, out Bounds bounds))
                {
                    continue;
                }

                Vector3 viewDirection = UnityEngine.Camera.main != null
                    ? UnityEngine.Camera.main.transform.position - bounds.center
                    : Vector3.forward;
                viewDirection.y = 0f;
                if (viewDirection.sqrMagnitude < 0.001f)
                {
                    viewDirection = Vector3.forward;
                }

                viewDirection.Normalize();
                var tieRoot = new GameObject(PlaceholderTieName);
                tieRoot.transform.SetParent(alien.transform, true);
                tieRoot.transform.position =
                    bounds.center + Vector3.up * (bounds.extents.y * 0.2f) +
                    viewDirection * Mathf.Max(0.04f, bounds.extents.magnitude * 0.08f);
                tieRoot.transform.rotation = Quaternion.LookRotation(-viewDirection, Vector3.up);

                float scale = Mathf.Clamp(bounds.extents.y * 0.16f, 0.08f, 0.28f);
                GameObject knot = CreatePrimitivePart(
                    "Knot",
                    tieRoot.transform,
                    new Vector3(0f, scale * 0.48f, 0f),
                    new Vector3(scale * 0.55f, scale * 0.42f, scale * 0.2f),
                    GetBlackMaterial());
                knot.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);

                GameObject body = CreatePrimitivePart(
                    "Tie",
                    tieRoot.transform,
                    new Vector3(0f, -scale * 0.48f, 0f),
                    new Vector3(scale * 0.5f, scale * 1.35f, scale * 0.16f),
                    GetBlackMaterial());
                body.transform.localRotation = Quaternion.Euler(0f, 0f, 4f);
            }
        }

        private void EnsurePlaceholderDoor(Vector3 center, Vector3 forward)
        {
            if (_placeholderDoor != null)
            {
                return;
            }

            _placeholderDoor = new GameObject(PlaceholderDoorName);
            _placeholderDoor.transform.position = center;
            Vector3 look = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            _placeholderDoor.transform.rotation = Quaternion.LookRotation(look, Vector3.up);

            float width = Mathf.Max(2.4f, GetEnsembleWidth() + 0.8f);
            float height = Mathf.Max(2.3f, GetEnsembleHeight() + 0.35f);
            float frame = 0.14f;
            CreatePrimitivePart(
                "Left",
                _placeholderDoor.transform,
                new Vector3(-width * 0.5f, height * 0.5f, 0f),
                new Vector3(frame, height, frame),
                GetDoorMaterial());
            CreatePrimitivePart(
                "Right",
                _placeholderDoor.transform,
                new Vector3(width * 0.5f, height * 0.5f, 0f),
                new Vector3(frame, height, frame),
                GetDoorMaterial());
            CreatePrimitivePart(
                "Top",
                _placeholderDoor.transform,
                new Vector3(0f, height, 0f),
                new Vector3(width + frame, frame, frame),
                GetDoorMaterial());
        }

        private float GetEnsembleWidth()
        {
            if (_aliens.Count < 2)
            {
                return 1.2f;
            }

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = 0; i < _aliens.Count; i++)
            {
                if (_aliens[i] == null)
                {
                    continue;
                }

                min = Mathf.Min(min, _aliens[i].transform.position.x);
                max = Mathf.Max(max, _aliens[i].transform.position.x);
            }

            return float.IsInfinity(min) ? 1.2f : Mathf.Max(1.2f, max - min);
        }

        private float GetEnsembleHeight()
        {
            float height = 2f;
            for (int i = 0; i < _aliens.Count; i++)
            {
                if (_aliens[i] != null && TryGetWorldBounds(_aliens[i].gameObject, out Bounds bounds))
                {
                    height = Mathf.Max(height, bounds.size.y);
                }
            }

            return height;
        }

        private Material GetBlackMaterial()
        {
            if (_blackMaterial == null)
            {
                _blackMaterial = CreateMaterial(new Color(0.015f, 0.018f, 0.022f, 1f));
            }

            return _blackMaterial;
        }

        private Material GetDoorMaterial()
        {
            if (_doorMaterial == null)
            {
                _doorMaterial = CreateMaterial(new Color(0.12f, 0.15f, 0.18f, 1f));
            }

            return _doorMaterial;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                color = color,
                name = "FirstContactPlaceholderMaterial"
            };
            return material;
        }

        private static GameObject CreatePrimitivePart(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (part.TryGetComponent(out Collider collider))
            {
                Object.Destroy(collider);
            }

            if (part.TryGetComponent(out Renderer renderer))
            {
                renderer.sharedMaterial = material;
            }

            return part;
        }

        private static bool TryGetWorldBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
            {
                return false;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || IsTemporaryRenderer(renderer.transform))
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private static bool IsTemporaryRenderer(Transform current)
        {
            while (current != null)
            {
                if (current.name.StartsWith("FC_TEMP_"))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }
    }
}
