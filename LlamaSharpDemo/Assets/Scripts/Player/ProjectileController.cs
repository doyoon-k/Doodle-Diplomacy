using UnityEngine;

public class ProjectileController : MonoBehaviour
{
    [Header("Settings")]
    public float speed = 10f;
    public float lifeTime = 3f;
    public float damage = 10f;
    public string element = "Physical";

    [Header("Special Properties")]
    private bool isExplosive = false;
    private float explosionRadius = 50f;
    private bool isPiercing = false;
    private int pierceCount = 0;
    private int maxPierceCount = 3;

    [Header("Effects")]
    public GameObject explosionEffectPrefab;

    private Rigidbody2D rb;
    private Vector2 moveDir;

    public void Initialize(Vector2 direction, float dmg, string elem, Color color)
    {
        moveDir = direction;
        damage = dmg;
        element = elem;

        GetComponent<SpriteRenderer>().color = color;
        Destroy(gameObject, lifeTime);
    }
    public void SetExplosive(float radius)
    {
        isExplosive = true;
        explosionRadius = radius;
    }
    public void SetPiercing(int maxPierces)
    {
        isPiercing = true;
        maxPierceCount = maxPierces;
        pierceCount = 0;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (rb != null)
        {
            rb.linearVelocity = moveDir * speed;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Enemy"))
        {
            if (isExplosive)
            {
                Explode();
            }
            else
            {
                PlayerStats enemyStats = collision.GetComponent<PlayerStats>();
                if (enemyStats != null)
                {
                    enemyStats.TakeDamage(damage);
                    Debug.Log($"[Projectile] Hit {collision.name}! Dealt {damage} {element} damage.");
                }

                if (isPiercing)
                {
                    pierceCount++;
                    if (pierceCount >= maxPierceCount)
                    {
                        Debug.Log($"[Piercing] Max pierce reached!");
                        Destroy(gameObject);
                    }
                    else
                    {
                        Debug.Log($"[Piercing] Pierce {pierceCount}/{maxPierceCount}");
                    }
                }
                else
                {
                    Destroy(gameObject);
                }
            }
        }
        else if (collision.CompareTag("Ground"))
        {
            if (isExplosive)
            {
                Explode();
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    void Explode()
    {
        Debug.Log($"[Explosive] BOOM! Radius: {explosionRadius}");

        if (explosionEffectPrefab != null)
        {
            GameObject effect = Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);

            ExplosionEffect explosionEffect = effect.GetComponent<ExplosionEffect>();
            if (explosionEffect != null)
            {
                explosionEffect.maxScale = explosionRadius / 10f;
            }
        }

        Collider2D[] targets = Physics2D.OverlapCircleAll(transform.position, explosionRadius, LayerMask.GetMask("Enemy"));

        foreach (Collider2D col in targets)
        {
            PlayerStats targetStats = col.GetComponent<PlayerStats>();
            if (targetStats != null)
            {
                targetStats.TakeDamage(damage);
                Debug.Log($"[Explosive] Hit {col.name} for {damage}!");
            }

            Rigidbody2D targetRb = col.GetComponent<Rigidbody2D>();
            if (targetRb != null)
            {
                Vector2 knockbackDir = (col.transform.position - transform.position).normalized;
                targetRb.AddForce(knockbackDir * 100f, ForceMode2D.Impulse);
            }
        }

        Destroy(gameObject);
    }
}