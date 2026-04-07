using UnityEngine;

namespace DoodleDiplomacy.Core
{
    // 테스트용 — Play 진입 시 StartGame 자동 호출. 완료 후 제거.
    public class GameTestStarter : MonoBehaviour
    {
        [Tooltip("false = WaitingForRound부터 시작 (빠른 테스트), true = Intro부터 시작")]
        [SerializeField] private bool isFirstPlay = false;

        private void Start()
        {
            if (RoundManager.Instance != null)
                RoundManager.Instance.StartGame(isFirstPlay);
            else
                Debug.LogError("[GameTestStarter] RoundManager.Instance가 없습니다!");
        }
    }
}
