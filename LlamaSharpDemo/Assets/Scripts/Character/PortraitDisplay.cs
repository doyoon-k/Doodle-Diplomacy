using UnityEngine;
using DoodleDiplomacy.Data;

namespace DoodleDiplomacy.Character
{
    public class PortraitDisplay : MonoBehaviour
    {
        [SerializeField] private PortraitSet portraitSet;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private int materialIndex = 0;
        [Tooltip("URP: _BaseMap / Legacy Standard: _MainTex")]
        [SerializeField] private string texturePropertyName = "_BaseMap";

        private Material _materialInstance;
        private Texture2D _defaultTexture;

        private void Awake()
        {
            if (targetRenderer == null)
                targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null)
            {
                Debug.LogWarning($"[PortraitDisplay] '{name}': No Renderer found.", this);
                return;
            }

            // 공유 머티리얼을 인스턴스화해 다른 오브젝트에 영향 주지 않도록
            var shared = targetRenderer.sharedMaterials;
            _materialInstance = new Material(shared[materialIndex]);
            shared[materialIndex] = _materialInstance;
            targetRenderer.sharedMaterials = shared;

            _defaultTexture = _materialInstance.GetTexture(texturePropertyName) as Texture2D;
        }

        public void SetEmotion(string emotionID)
        {
            if (_materialInstance == null || portraitSet == null) return;
            var tex = portraitSet.GetPortrait(emotionID);
            if (tex != null)
                _materialInstance.SetTexture(texturePropertyName, tex);
        }

        public void SetDefault()
        {
            if (_materialInstance == null) return;
            _materialInstance.SetTexture(texturePropertyName, _defaultTexture);
        }

        // ── Inspector 컨텍스트 메뉴 테스트 버튼 ──

        [ContextMenu("Test: angry")]
        private void TestAngry() => SetEmotion("angry");

        [ContextMenu("Test: satisfied")]
        private void TestSatisfied() => SetEmotion("satisfied");

        [ContextMenu("Test: neutral")]
        private void TestNeutral() => SetEmotion("neutral");

        [ContextMenu("Test: default")]
        private void TestDefault() => SetDefault();
    }
}
