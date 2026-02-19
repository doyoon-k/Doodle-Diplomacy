using UnityEngine;

public class ExplosionEffect : MonoBehaviour
{
    [Header("Effect Settings")]
    public float maxScale = 3f;
    public float duration = 0.3f;
    public Color startColor = new Color(1f, 0.5f, 0f, 1f);
    public Color endColor = new Color(1f, 0f, 0f, 0f);    

    private SpriteRenderer spriteRenderer;
    private float elapsed = 0f;
    private Vector3 initialScale;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }

        if (spriteRenderer.sprite == null)
        {
            spriteRenderer.sprite = CreateCircleSprite();
        }

        initialScale = transform.localScale;
        spriteRenderer.color = startColor;
        spriteRenderer.sortingOrder = 100;

        Destroy(gameObject, duration + 0.1f);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;

        float scale = Mathf.Lerp(1f, maxScale, t);
        transform.localScale = initialScale * scale;

        spriteRenderer.color = Color.Lerp(startColor, endColor, t);
    }

    Sprite CreateCircleSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size);
        Color[] colors = new Color[size * size];

        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                if (dist < radius)
                {
                    float alpha = 1f - (dist / radius);
                    colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
                else
                {
                    colors[y * size + x] = Color.clear;
                }
            }
        }

        texture.SetPixels(colors);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }
}