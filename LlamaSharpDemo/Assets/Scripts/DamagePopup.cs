using UnityEngine;
using UnityEngine.UI;

public class DamagePopup : MonoBehaviour
{
    public Text damageText;
    public float lifetime = 1f;
    public float moveSpeed = 1f;
    public Color criticalColor = Color.red;
    public Color normalColor = Color.white;

    public bool isFloatingStatus = false;
    public Transform followTarget;
    public Vector3 offset = new Vector3(0, 2f, 0);

    private float timer = 0f;
    private Vector3 moveDirection;

    public void Initialize(float damage, bool isCritical = false)
    {
        isFloatingStatus = false;
        if (damageText != null)
        {
            damageText.text = damage.ToString("F0");
            damageText.color = isCritical ? criticalColor : normalColor;
            damageText.fontSize = isCritical ? 36 : 24;
        }

        moveDirection = Vector3.up + new Vector3(Random.Range(-0.3f, 0.3f), 0, 0);
    }

    public void InitializeStatus(Transform target, string text)
    {
        isFloatingStatus = true;
        followTarget = target;

        if (damageText != null)
        {
            damageText.text = text;
            damageText.color = Color.yellow; // Default color for status
            damageText.fontSize = 24;
            // Reset alpha
            Color c = damageText.color;
            c.a = 1f;
            damageText.color = c;
        }
    }

    public void SetText(string text)
    {
        if (damageText != null) damageText.text = text;
    }

    void Update()
    {
        if (isFloatingStatus)
        {
            if (followTarget != null)
            {
                transform.position = followTarget.position + offset;
            }
            else
            {
                Destroy(gameObject); // Destroy if target is lost
            }
            return; // Skip normal damage popup behavior
        }

        timer += Time.deltaTime;

        transform.position += moveDirection * moveSpeed * Time.deltaTime;

        if (damageText != null)
        {
            float alpha = 1f - (timer / lifetime);
            Color color = damageText.color;
            color.a = alpha;
            damageText.color = color;
        }

        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }
}