using UnityEngine;

public class SlashEffect : MonoBehaviour
{
    [Header("Effect Settings")]
    public float duration = 0.08f;
    public Color slashColor = new Color(1f, 1f, 1f, 0.9f);

    private SpriteRenderer spriteRenderer;
    private float elapsed = 0f;
    private Vector3 startScale;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = CreateSharpSlash();
        }

        spriteRenderer.color = slashColor;
        spriteRenderer.sortingOrder = 100;
        startScale = transform.localScale;

        Destroy(gameObject, duration);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;

        Color c = slashColor;
        c.a = slashColor.a * (1f - t * t); 
        spriteRenderer.color = c;

        transform.localScale = startScale * (1f + t * 0.3f);
    }

    Sprite CreateSharpSlash()
    {
        int width = 80;
        int height = 40;
        Texture2D texture = new Texture2D(width, height);
        Color[] colors = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (float)x / width;
                float ny = (float)y / height - 0.5f;

                float curve = Mathf.Sin(nx * Mathf.PI) * 0.4f;
                float thickness = 0.08f * (1f - nx * 0.7f);
                float dist = Mathf.Abs(ny - curve);

                if (dist < thickness)
                {
                    float alpha = 1f - (dist / thickness);
                    alpha = alpha * alpha; 
                    alpha *= Mathf.Pow(nx, 0.3f);  
                    alpha *= (1f - Mathf.Pow(nx, 3f) * 0.5f);
                    colors[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    colors[y * width + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;

        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.2f, 0.5f), 50f);
    }
}