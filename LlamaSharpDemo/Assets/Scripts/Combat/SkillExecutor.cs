using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class SkillExecutor : MonoBehaviour
{
    [Header("Components")]
    public PlayerStats playerStats;
    public Rigidbody2D rb;
    public AttackHitbox attackHitbox;
    public SpriteRenderer spriteRenderer;

    [Header("Prefabs")]
    public GameObject projectilePrefab;
    public Transform firePoint;

    [Header("Movement Settings")]
    public float dashSpeed = 0.5f;
    public float dashDuration = 0.01f;
    public float jumpForce = 12f;
    public float blinkDistance = 120f;
    public bool IsDashing() => isDashing;

    [Header("Attack Settings")]
    public float attackDelay = 0.2f;
    public float projectileCooldown = 1.0f;
    private float lastProjectileTime = -999f;

    [Header("State Tracking")]
    private bool isInvincible = false;
    private bool isFacingRight = true;
    private bool isDashing = false;
    [Header("Facing Direction")]
    [Tooltip("Ignore tiny horizontal input/noise below this value when updating facing direction.")]
    public float facingInputDeadZone = 0.2f;
    [Tooltip("Fallback velocity deadzone used when input is near zero.")]
    public float facingVelocityDeadZone = 0.05f;

    [Header("MultiJump Settings")]
    public int maxExtraJumps = 1;
    private int remainingJumps;
    private bool isGrounded;

    [Header("Ground Check")]
    public PlayerController playerController;

    [Header("Blink Settings")]
    public LayerMask blinkObstacleLayer;
    public GameObject blinkEffectPrefab;

    [Header("MeleeStrike Settings")]
    public float meleeHitboxDuration = 0.2f;
    public float meleeDamageMultiplier = 1.5f;
    public GameObject slashEffectPrefab;
    public Vector2 slashOffset = new Vector2(15f, 0f);

    [Header("Defense State")]
    public float currentShield = 0f;
    public float damageReductionMultiplier = 1f;

    [Header("Defense Settings")]
    public float shieldAmount = 500f;
    public float shieldDuration = 10f;
    public float invincibilityDuration = 2f;
    public float damageReduction = 0.5f;
    public float damageReductionDuration = 5f;

    [Header("Utility Settings")]
    public float stunRadius = 50f;
    public float stunDuration = 2f;
    public float slowRadius = 50f;
    public float slowMultiplier = 0.3f;
    public float slowDuration = 3f;
    public float airborneRadius = 50f;
    public float airborneForce = 300f;

    [Header("Targeting")]
    [Tooltip("Preferred physics mask for enemy targeting. If empty, falls back to all colliders + EnemyAI/tag filtering.")]
    public LayerMask enemyTargetMask;
    [Tooltip("Optional fallback tag check for enemy targets.")]
    public string enemyTag = "Enemy";

    [Header("GroundSlam Settings")]
    public float groundSlamForce = 500f;
    public float groundSlamRadius = 80f;
    public float groundSlamDamageMultiplier = 2f;

    [Header("Projectile Settings")]
    public float explosiveRadius = 80f;
    public float explosiveDamageMultiplier = 1.5f;
    public int pierceMaxCount = 3;
    public float piercingDamageMultiplier = 0.8f;

    private Coroutine shieldCoroutine;
    private Coroutine invincibleCoroutine;
    private Coroutine damageReductionCoroutine;
    private Color baseSpriteColor = Color.white;



    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (playerStats == null) playerStats = GetComponent<PlayerStats>();
        if (attackHitbox == null) attackHitbox = GetComponentInChildren<AttackHitbox>();
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (playerController == null) playerController = GetComponent<PlayerController>();

        if (spriteRenderer != null)
        {
            baseSpriteColor = spriteRenderer.color;
            isFacingRight = !spriteRenderer.flipX;
        }
        else if (transform.localScale.x < 0f)
        {
            isFacingRight = false;
        }

        if (attackHitbox != null) attackHitbox.gameObject.SetActive(false);
        remainingJumps = maxExtraJumps;
    }

    void Update()
    {
        if (playerController != null && !playerController.IsInputEnabled) return;

        float moveInput = DemoInput.GetAxisRaw("Horizontal");
        if (!isDashing)
        {
            UpdateFacingDirection(moveInput);
        }

        if (spriteRenderer != null && Mathf.Abs(moveInput) > 0.1f)
        {
            spriteRenderer.flipX = !isFacingRight;
        }

        if (DemoInput.GetKeyDown(KeyCode.K))
        {
            if (Time.time - lastProjectileTime >= projectileCooldown)
            {
                lastProjectileTime = Time.time;
                FireProjectile("Physical", Color.white);
            }
        }

        if (DemoInput.GetKeyDown(KeyCode.P))
        {
            StartCoroutine(MeleeStrike());
        }

        if (DemoInput.GetKeyDown(KeyCode.O))
        {
            StartCoroutine(FireProjectileCoroutine());
        }

        if (DemoInput.GetKeyDown(KeyCode.I))
        {
            StartCoroutine(PiercingProjectile());
        }

        if (DemoInput.GetKeyDown(KeyCode.U))
        {
            StartCoroutine(ExplosiveProjectile());
        }
    }

    public float GetProjectileCooldown()
    {
        return Mathf.Max(0f, projectileCooldown - (Time.time - lastProjectileTime));
    }

    public IEnumerator ExecuteSkill(SkillData skill)
    {
        Debug.Log($"=== Executing Skill: {skill.name} ===");

        foreach (string atomicSkill in skill.sequence)
        {
            yield return ExecuteAtomicSkill(atomicSkill);
            yield return new WaitForSeconds(0.05f);
        }
    }

    IEnumerator ExecuteAtomicSkill(string atomicSkill)
    {
        string action = NormalizeAtomicSkill(atomicSkill);

        switch (action)
        {
            // ===== ATTACK =====
            case "FIREPROJECTILE": yield return FireProjectileCoroutine(); break;
            case "EXPLOSIVEPROJECTILE": yield return ExplosiveProjectile(); break;
            case "PIERCINGPROJECTILE": yield return PiercingProjectile(); break;
            case "MELEESTRIKE": yield return MeleeStrike(); break;
            case "GROUNDSLAM": yield return GroundSlam(); break;

            // ===== MOVE =====
            case "DASH": yield return Dash(); break;
            case "MULTIJUMP": yield return MultiJump(); break;
            case "BLINK": yield return Blink(); break;

            // ===== DEFENCE =====
            case "SHIELDBUFF": yield return ShieldBuff(); break;
            case "INSTANTHEAL": yield return InstantHeal(); break;
            case "INVINCIBLE": yield return Invincible(); break;
            case "DAMAGEREDUCTIONBUFF": yield return DamageReductionBuff(); break;

            // ===== Utility =====
            case "STUN": yield return Stun(); break;
            case "SLOW": yield return Slow(); break;
            case "AIRBORNE": yield return Airborne(); break;
            default:
                Debug.LogWarning($"[SkillExecutor] Unknown primitive action: '{atomicSkill}'");
                break;
        }
    }

    // ========================================
    // Movement-related primitive Skills
    // ========================================

    IEnumerator Dash()
    {
        isDashing = true;
        float startTime = Time.time;
        float originalGravity = rb.gravityScale;

        // VFX: Dash trail
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateDashEffect(transform);
        }

        rb.gravityScale = 0;
        Vector2 dir = isFacingRight ? Vector2.right : Vector2.left;

        while (Time.time < startTime + dashDuration)
        {
            rb.linearVelocity = dir * dashSpeed;
            yield return null;
        }

        rb.linearVelocity = Vector2.zero;
        rb.gravityScale = originalGravity;
        isDashing = false;
    }

    IEnumerator MultiJump()
    {
        // Jump 1
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        float jumpPower = playerStats != null ? playerStats.GetStat("JumpPower") : jumpForce;
        rb.AddForce(Vector2.up * jumpPower, ForceMode2D.Impulse);

        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateJumpBurst(transform.position);
        }
        Debug.Log("MultiJump: Jump 1!");

        // Wait for a short duration to allow upward movement
        yield return new WaitForSeconds(0.2f);

        // Jump 2 (Air Jump)
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f); // Reset Y velocity for consistent second jump
        rb.AddForce(Vector2.up * jumpPower, ForceMode2D.Impulse);

        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateJumpBurst(transform.position);
        }
        Debug.Log("MultiJump: Jump 2!");

        yield return new WaitForSeconds(0.1f);
    }

    IEnumerator Blink()
    {
        Vector2 blinkDir = isFacingRight ? Vector2.right : Vector2.left;
        Vector2 startPos = transform.position;
        Vector2 targetPos = startPos + blinkDir * blinkDistance;

        int layerMask = ~LayerMask.GetMask("Player", "Enemy");
        RaycastHit2D hit = Physics2D.Raycast(startPos, blinkDir, blinkDistance, layerMask);

        if (hit.collider != null)
        {
            targetPos = hit.point - blinkDir * 5f;
            Debug.Log($"WALL! {hit.collider.name}");
        }

        // VFX: Blink effect at start and end positions
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateBlinkEffect(startPos, targetPos, transform);
        }

        transform.position = targetPos;
        Debug.Log($"Blink! {startPos} -> {targetPos}");
        yield return new WaitForSeconds(0.1f);
    }

    // ========================================
    // Attack-related primitive Skills
    // ========================================

    // NEED TO FIX
    void FireProjectile(string element, Color color)
    {
        if (projectilePrefab == null || firePoint == null) return;

        GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
        ProjectileController pc = proj.GetComponent<ProjectileController>();

        if (pc != null)
        {
            Vector2 dir = isFacingRight ? Vector2.right : Vector2.left;
            float dmg = (playerStats != null) ? playerStats.GetStat("AttackPower") : 10f;
            pc.Initialize(dir, dmg, element, color);

            // VFX: Fire trail on normal projectile
            if (ProceduralVFX.Instance != null)
            {
                ProceduralVFX.Instance.CreateFireTrail(proj.transform);
            }
        }
    }

    IEnumerator FireProjectileCoroutine()
    {
        FireProjectile("Normal", Color.white);
        yield return new WaitForSeconds(0.1f);
    }

    IEnumerator ExplosiveProjectile()
    {
        if (projectilePrefab == null || firePoint == null) yield break;

        GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
        ProjectileController pc = proj.GetComponent<ProjectileController>();

        if (pc != null)
        {
            Vector2 dir = isFacingRight ? Vector2.right : Vector2.left;
            float dmg = (playerStats != null) ? playerStats.GetStat("AttackPower") * explosiveDamageMultiplier : 15f;
            pc.Initialize(dir, dmg, "Explosive", Color.red);
            pc.SetExplosive(explosiveRadius);

            // VFX: Fire trail on projectile
            if (ProceduralVFX.Instance != null)
            {
                ProceduralVFX.Instance.CreateFireTrail(proj.transform);
            }
        }

        Debug.Log("ExplosiveProjectile fired!");
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator PiercingProjectile()
    {
        if (projectilePrefab == null || firePoint == null) yield break;

        GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.identity);
        ProjectileController pc = proj.GetComponent<ProjectileController>();

        if (pc != null)
        {
            Vector2 dir = isFacingRight ? Vector2.right : Vector2.left;
            float dmg = (playerStats != null) ? playerStats.GetStat("AttackPower") * piercingDamageMultiplier : 8f;
            pc.Initialize(dir, dmg, "Piercing", Color.cyan);
            pc.SetPiercing(pierceMaxCount);

            // VFX: Beam effect
            if (ProceduralVFX.Instance != null && firePoint != null)
            {
                Vector3 endPos = firePoint.position + (isFacingRight ? Vector3.right : Vector3.left) * 15f;
                ProceduralVFX.Instance.CreateBeam(firePoint.position, endPos, 0.2f);
            }
        }

        Debug.Log("PiercingProjectile fired!");
        yield return new WaitForSeconds(0.1f);
    }

    IEnumerator MeleeStrike()
    {
        Debug.Log($"MeleeStrike! isFacingRight: {isFacingRight}");

        if (attackHitbox == null)
        {
            Debug.LogWarning("No AttackHitbox!");
            yield break;
        }

        // VFX: Slash arc
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateSlashArc(transform, isFacingRight);
        }

        Vector2 offset = attackHitbox.hitboxOffset;
        offset.x = isFacingRight ? Mathf.Abs(offset.x) : -Mathf.Abs(offset.x);
        attackHitbox.hitboxOffset = offset;

        if (slashEffectPrefab != null)
        {
            Vector3 effectPos = transform.position + new Vector3(
                isFacingRight ? slashOffset.x : -slashOffset.x,
                slashOffset.y,
                0f
            );

            float zRotation = isFacingRight ? -90f : 90f;
            Quaternion rotation = Quaternion.Euler(0f, 0f, zRotation);

            Instantiate(slashEffectPrefab, effectPos, rotation);
        }

        attackHitbox.SetDamageMultiplier(meleeDamageMultiplier);

        attackHitbox.gameObject.SetActive(true);
        attackHitbox.ActivateHitbox(meleeHitboxDuration);

        Debug.Log($"MeleeStrike! Multiplier: {meleeDamageMultiplier}");
        yield return new WaitForSeconds(meleeHitboxDuration + 0.1f);

        attackHitbox.ResetDamageMultiplier();
        attackHitbox.gameObject.SetActive(false);
    }

    IEnumerator GroundSlam()
    {
        bool grounded = playerController != null && playerController.IsGrounded();

        if (grounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
            rb.AddForce(Vector2.up * jumpForce * 0.5f, ForceMode2D.Impulse);
            yield return new WaitForSeconds(0.15f);
        }

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, -groundSlamForce);

        float timeout = 2f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (playerController != null && playerController.IsGrounded())
            {
                break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        Debug.Log("GroundSlam Impact!");

        // VFX: Shockwave rings
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateShockwave(transform.position);
        }

        Collider2D[] targets = Physics2D.OverlapCircleAll(transform.position, groundSlamRadius, LayerMask.GetMask("Enemy"));
        if (targets == null || targets.Length == 0)
        {
            targets = FindEnemyTargets(groundSlamRadius);
        }
        float damage = (playerStats != null) ? playerStats.GetStat("AttackPower") * groundSlamDamageMultiplier : 100f;

        foreach (Collider2D col in targets)
        {
            PlayerStats targetStats = col.GetComponent<PlayerStats>();
            if (targetStats != null)
            {
                targetStats.TakeDamage(damage);
                Debug.Log($"GroundSlam hit {col.name} for {damage}!");
            }

            Rigidbody2D targetRb = col.GetComponent<Rigidbody2D>();
            if (targetRb != null)
            {
                Vector2 knockbackDir = (col.transform.position - transform.position).normalized;
                targetRb.AddForce(knockbackDir * 200f, ForceMode2D.Impulse);
            }
        }

        yield return new WaitForSeconds(0.2f);
    }

    // ========================================
    // Defense-related primitive Skills
    // ========================================
    IEnumerator ShieldBuff()
    {
        if (shieldCoroutine != null) StopCoroutine(shieldCoroutine);

        currentShield = shieldAmount;
        Debug.Log($"Shield activated! {currentShield} damage absorption");

        // VFX: Shield ring
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateShieldRing(transform, shieldDuration);
        }

        shieldCoroutine = StartCoroutine(MonitorShieldDuration());
        yield return null;
    }

    IEnumerator MonitorShieldDuration()
    {
        float elapsed = 0f;
        while (elapsed < shieldDuration && currentShield > 0f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        currentShield = 0f;
        Debug.Log("Shield expired!");
        shieldCoroutine = null;
    }

    IEnumerator InstantHeal()
    {
        if (playerStats == null) yield break;

        float healAmount = playerStats.GetStat("MaxHealth") * 0.2f;
        playerStats.Heal(healAmount);

        // VFX: Heal sparkles
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateHealEffect(transform.position);
        }

        Debug.Log($"Healed {healAmount} HP!");
        yield return new WaitForSeconds(0.2f);
    }

    IEnumerator Invincible()
    {
        if (invincibleCoroutine != null) StopCoroutine(invincibleCoroutine);

        isInvincible = true;
        Debug.Log($"Invincible for {invincibilityDuration}s!");

        // VFX: Invulnerability flash
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateInvulnerabilityFlash(transform, invincibilityDuration);
        }

        invincibleCoroutine = StartCoroutine(MonitorInvincibility());
        yield return null;
    }

    IEnumerator MonitorInvincibility()
    {
        float elapsed = 0f;
        float flashInterval = 0.1f;

        while (elapsed < invincibilityDuration)
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.color = spriteRenderer.color.a > 0.5f
                    ? new Color(baseSpriteColor.r, baseSpriteColor.g, baseSpriteColor.b, 0.3f)
                    : baseSpriteColor;
            }

            elapsed += flashInterval;
            yield return new WaitForSeconds(flashInterval);
        }

        isInvincible = false;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = baseSpriteColor;
        }
        Debug.Log("Invincibility ended!");
        invincibleCoroutine = null;
    }

    IEnumerator DamageReductionBuff()
    {
        if (damageReductionCoroutine != null) StopCoroutine(damageReductionCoroutine);

        damageReductionMultiplier = 1f - damageReduction;
        Debug.Log($"Damage reduced to {damageReductionMultiplier * 100}% for {damageReductionDuration}s!");

        // VFX: Hexagon barrier
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateBarrierHexagon(transform, damageReductionDuration);
        }

        damageReductionCoroutine = StartCoroutine(MonitorDamageReduction());
        yield return null;
    }

    IEnumerator MonitorDamageReduction()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = new Color(0.5f, 0.5f, 1f);
        }

        yield return new WaitForSeconds(damageReductionDuration);

        damageReductionMultiplier = 1f;
        if (spriteRenderer != null)
        {
            spriteRenderer.color = baseSpriteColor;
        }
        Debug.Log("Damage reduction ended!");
        damageReductionCoroutine = null;
    }

    public float ProcessIncomingDamage(float damage)
    {
        if (isInvincible)
        {
            Debug.Log("Damage blocked by invincibility!");
            return 0f;
        }

        float reducedDamage = damage * damageReductionMultiplier;

        if (currentShield > 0f)
        {
            if (currentShield >= reducedDamage)
            {
                currentShield -= reducedDamage;
                Debug.Log($"Shield absorbed {reducedDamage} damage! Shield remaining: {currentShield}");
                return 0f;
            }
            else
            {
                reducedDamage -= currentShield;
                Debug.Log($"Shield broke! {currentShield} absorbed, {reducedDamage} damage passes through");
                currentShield = 0f;
            }
        }

        return reducedDamage;
    }

    // ========================================
    // Utility-related primitive Skills
    // ========================================

    IEnumerator Stun()
    {
        Debug.Log("Stun!");

        Collider2D[] targets = FindEnemyTargets(stunRadius);
        if (targets.Length == 0)
        {
            Debug.Log("[SkillExecutor] Stun found no enemy targets in range.");
        }

        foreach (Collider2D col in targets)
        {
            EnemyAI enemyAI = col.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.ApplyStun(stunDuration);

                // VFX: Stun stars on target
                if (ProceduralVFX.Instance != null)
                {
                    ProceduralVFX.Instance.CreateStunStars(col.transform, stunDuration);
                }
            }
        }

        yield return new WaitForSeconds(0.1f);
    }

    IEnumerator Slow()
    {
        Debug.Log("Slow!");

        Collider2D[] targets = FindEnemyTargets(slowRadius);
        if (targets.Length == 0)
        {
            Debug.Log("[SkillExecutor] Slow found no enemy targets in range.");
        }

        foreach (Collider2D col in targets)
        {
            EnemyAI enemyAI = col.GetComponent<EnemyAI>();
            if (enemyAI != null)
            {
                enemyAI.ApplySlow(slowMultiplier, slowDuration);

                // VFX: Slow ice particles on target
                if (ProceduralVFX.Instance != null)
                {
                    ProceduralVFX.Instance.CreateSlowEffect(col.transform, slowDuration);
                }
            }
        }

        yield return new WaitForSeconds(0.1f);
    }

    IEnumerator Airborne()
    {
        Debug.Log("Airborne!");

        // VFX: Airborne wind effect
        if (ProceduralVFX.Instance != null)
        {
            ProceduralVFX.Instance.CreateAirborneEffect(transform.position, airborneForce);
        }

        Collider2D[] targets = FindEnemyTargets(airborneRadius);
        if (targets.Length == 0)
        {
            Debug.Log("[SkillExecutor] Airborne found no enemy targets in range.");
        }

        foreach (Collider2D col in targets)
        {
            Rigidbody2D targetRb = col.GetComponent<Rigidbody2D>();
            if (targetRb != null)
            {
                targetRb.linearVelocity = new Vector2(targetRb.linearVelocity.x, 0f);
                targetRb.AddForce(Vector2.up * airborneForce, ForceMode2D.Impulse);
                Debug.Log($"Launched {col.name} into the air!");
            }
        }

        yield return new WaitForSeconds(0.1f);
    }

    // ========================================
    // HELPER FUNCTIONS
    // ========================================

    private string NormalizeAtomicSkill(string atomicSkill)
    {
        if (string.IsNullOrWhiteSpace(atomicSkill))
        {
            return string.Empty;
        }

        return atomicSkill
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .ToUpperInvariant();
    }

    private Collider2D[] FindEnemyTargets(float radius)
    {
        Collider2D[] candidates = enemyTargetMask.value != 0
            ? Physics2D.OverlapCircleAll(transform.position, radius, enemyTargetMask)
            : Physics2D.OverlapCircleAll(transform.position, radius);

        if (candidates == null || candidates.Length == 0)
        {
            return System.Array.Empty<Collider2D>();
        }

        var filtered = new List<Collider2D>(candidates.Length);
        foreach (Collider2D col in candidates)
        {
            if (col == null || col.transform == transform)
            {
                continue;
            }

            bool isEnemy = col.GetComponent<EnemyAI>() != null;
            if (!isEnemy && !string.IsNullOrWhiteSpace(enemyTag))
            {
                try
                {
                    isEnemy = col.CompareTag(enemyTag);
                }
                catch (UnityException)
                {
                    isEnemy = false;
                }
            }

            if (isEnemy)
            {
                filtered.Add(col);
            }
        }

        return filtered.ToArray();
    }

    private void UpdateFacingDirection(float horizontalInput)
    {
        if (horizontalInput > facingInputDeadZone)
        {
            isFacingRight = true;
            return;
        }

        if (horizontalInput < -facingInputDeadZone)
        {
            isFacingRight = false;
            return;
        }

        if (rb == null)
        {
            return;
        }

        float vx = rb.linearVelocity.x;
        if (vx > facingVelocityDeadZone)
        {
            isFacingRight = true;
        }
        else if (vx < -facingVelocityDeadZone)
        {
            isFacingRight = false;
        }
    }
}
