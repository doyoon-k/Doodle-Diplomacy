using System.Collections.Generic;
using UnityEngine;

public class AttackHitbox : MonoBehaviour
{
    [Header("Attack Properties")]
    public float damage = 10f;
    public float damageMultiplier = 1f;
    public float knockbackForce = 0.3f;
    public LayerMask enemyLayer;

    [Header("Hitbox Settings")]
    public Vector2 hitboxSize = new Vector2(50f, 50f);
    public Vector2 hitboxOffset = new Vector2(30f, 0f);

    private PlayerStats ownerStats;
    private bool isActive = false;
    private HashSet<Collider2D> hitTargets = new HashSet<Collider2D>();

    public void Initialize(PlayerStats stats)
    {
        ownerStats = stats;
    }

    public void ActivateHitbox(float duration)
    {
        isActive = true;
        hitTargets.Clear();
        Invoke(nameof(DeactivateHitbox), duration);
        Debug.Log("Hitbox activated!");
    }

    void DeactivateHitbox()
    {
        isActive = false;
        hitTargets.Clear();
        Debug.Log("Hitbox deactivated!");
    }

    void FixedUpdate()
    {
        if (!isActive) return;

        Vector2 hitboxPosition = (Vector2)transform.position + hitboxOffset;
        Collider2D[] hits = Physics2D.OverlapBoxAll(hitboxPosition, hitboxSize, 0f, enemyLayer);

        Debug.Log($"Hitbox checking at {hitboxPosition}, size: {hitboxSize}, enemyLayer: {enemyLayer.value}, found: {hits.Length}");

        foreach (Collider2D hit in hits)
        {
            if (hitTargets.Contains(hit)) continue;

            hitTargets.Add(hit);
            Debug.Log($"Hit detected: {hit.gameObject.name}");

            PlayerStats enemyStats = hit.GetComponent<PlayerStats>();
            if (enemyStats != null && ownerStats != null)
            {
                float totalDamage = ownerStats.GetStat("AttackPower") * damageMultiplier;
                enemyStats.TakeDamage(totalDamage);

                Rigidbody2D enemyRb = hit.GetComponent<Rigidbody2D>();
                if (enemyRb != null)
                {
                    Vector2 knockbackDirection = (hit.transform.position - transform.position).normalized;
                    enemyRb.AddForce(knockbackDirection * knockbackForce, ForceMode2D.Impulse);
                    Debug.Log($"Applied knockback: {knockbackDirection * knockbackForce}");
                }

                EnemyAI enemyAI = hit.GetComponent<EnemyAI>();
                if (enemyAI != null)
                {
                    enemyAI.OnHit();
                }
            }
        }
    }
    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = multiplier;
    }

    public void ResetDamageMultiplier()
    {
        damageMultiplier = 1f;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = isActive ? Color.red : Color.yellow;
        Vector2 hitboxPosition = (Vector2)transform.position + hitboxOffset;
        Gizmos.DrawWireCube(hitboxPosition, hitboxSize);
    }
}