using TMPro;
using UnityEngine;

namespace DoodleDiplomacy.Dialogue
{
    public class SubtitleDisplay : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI bodyText;

        private void Awake()
        {
            Hide();
        }

        /// <summary>
        /// 화면 하단 자막 패널을 열고 캐릭터명 + 대사 초기값을 설정.
        /// </summary>
        public void Show(string characterName, string text)
        {
            panel.SetActive(true);
            if (nameText != null)
                nameText.text = string.IsNullOrEmpty(characterName) ? "" : characterName;
            SetText(text);
        }

        public void SetText(string text)
        {
            if (bodyText != null)
                bodyText.text = text;
        }

        public void Hide()
        {
            if (panel != null)
                panel.SetActive(false);
        }
    }
}
