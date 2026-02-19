using UnityEngine;
using UnityEngine.UI;

public class SkillNotification : MonoBehaviour
{
    public Text skillNameText;
    public Text skillDescriptionText;
    public CanvasGroup canvasGroup;

    public float displayDuration = 2f;
    public float fadeSpeed = 2f;

    private float timer = 0f;
    private bool isShowing = false;

    void Update()
    {
        if (isShowing)
        {
            timer += Time.deltaTime;

            if (timer < 0.5f)
            {
                canvasGroup.alpha = Mathf.Lerp(0, 1, timer * 2f);
            }
            else if (timer > displayDuration - 0.5f)
            {
                canvasGroup.alpha = Mathf.Lerp(1, 0, (timer - (displayDuration - 0.5f)) * 2f);
            }

            if (timer >= displayDuration)
            {
                isShowing = false;
                canvasGroup.alpha = 0;
            }
        }
    }

    public void ShowSkill(string skillName, string description)
    {
        skillNameText.text = skillName;
        skillDescriptionText.text = description;
        timer = 0f;
        isShowing = true;
        canvasGroup.alpha = 0;
    }
}