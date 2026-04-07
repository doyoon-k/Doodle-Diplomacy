using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Dialogue
{
    public class WorldSpaceDialogue : MonoBehaviour
    {
        [SerializeField] private TextMeshPro textMesh;
        [SerializeField] private float verticalOffset = 2.5f;

        private Transform _anchor;
        private UnityEngine.Camera _mainCamera;

        private void Awake()
        {
            _mainCamera = UnityEngine.Camera.main;
            if (textMesh == null)
                textMesh = GetComponentInChildren<TextMeshPro>();
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_anchor != null)
                transform.position = _anchor.position + Vector3.up * verticalOffset;

            // 항상 카메라를 향하는 빌보드
            if (_mainCamera != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - _mainCamera.transform.position);
        }

        /// <summary>
        /// anchor 위에 텍스트를 표시. anchor가 null이면 현재 위치를 유지.
        /// </summary>
        public void Show(string text, Transform anchor)
        {
            _anchor = anchor;
            gameObject.SetActive(true);
            SetText(text);
        }

        public void SetText(string text)
        {
            if (textMesh != null)
                textMesh.text = text;
        }

        public void Hide()
        {
            gameObject.SetActive(false);
            _anchor = null;
        }
    }
}
