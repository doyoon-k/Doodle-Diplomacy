using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactProbePreviewScanline : MonoBehaviour
    {
        [SerializeField] private RectTransform line;

        private bool _scanning;
        private float _phase;

        public void Configure(RectTransform scanline)
        {
            line = scanline;
        }

        public void SetScanning(bool scanning)
        {
            _scanning = scanning;
            _phase = 0f;
            if (line != null)
            {
                line.gameObject.SetActive(scanning);
            }
        }

        private void Update()
        {
            if (!_scanning || line == null)
            {
                return;
            }

            RectTransform parent = line.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float height = Mathf.Max(1f, parent.rect.height);
            _phase = (_phase + Time.deltaTime * 0.72f) % 1f;
            float y = Mathf.Lerp((height * 0.42f) - 2f, (-height * 0.42f) + 2f, _phase);
            line.anchoredPosition = new Vector2(0f, y);
        }
    }
}
