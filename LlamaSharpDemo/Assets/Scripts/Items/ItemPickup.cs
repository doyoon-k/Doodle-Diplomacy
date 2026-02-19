using UnityEngine;

public class ItemPickup : MonoBehaviour
{
    [Header("Settings")]
    public ItemData itemData;
    public float floatSpeed = 2f;
    public float floatHeight = 0.2f;

    private Vector3 startPos;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        startPos = transform.position;
        spriteRenderer = GetComponent<SpriteRenderer>();

        if (itemData != null && itemData.icon != null)
        {
            spriteRenderer.sprite = itemData.icon;
        }
    }

    void Update()
    {
        float newY = startPos.y + Mathf.Sin(Time.time * floatSpeed) * floatHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            if (ItemManager.Instance != null)
            {
                ItemManager.Instance.AddItem(itemData);
                Destroy(gameObject);
            }
        }
    }
}