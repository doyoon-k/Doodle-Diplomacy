using System.Collections;
using UnityEngine;

/// <summary>
/// Manages all procedural visual effects for skills
/// Singleton pattern for easy access from SkillExecutor
/// </summary>
public class ProceduralVFX : MonoBehaviour
{
    public static ProceduralVFX Instance { get; private set; }

    [Header("VFX Settings")]
    [Tooltip("Parent for all spawned VFX (for organization)")]
    public Transform vfxContainer;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            // Create container if not assigned
            if (vfxContainer == null)
            {
                GameObject container = new GameObject("VFX_Container");
                vfxContainer = container.transform;
                vfxContainer.SetParent(transform);
            }
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #region Attack Effects

    /// <summary>
    /// Fire projectile trail effect
    /// </summary>
    public GameObject CreateFireTrail(Transform projectile)
    {
        GameObject vfx = new GameObject("FireTrail_VFX");
        vfx.transform.SetParent(projectile);
        vfx.transform.localPosition = Vector3.zero;

        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, Color.yellow, 0.4f, 0.2f);

        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(VFXHelper.CreateGradient(Color.yellow, Color.red));

        var emission = ps.emission;
        emission.rateOverTime = 40;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 20f;
        shape.rotation = new Vector3(0, 0, 180); // Backward from movement

        return vfx;
    }

    /// <summary>
    /// Explosive projectile burst on impact
    /// </summary>
    public void CreateExplosion(Vector3 position, float radius = 2f)
    {
        StartCoroutine(ExplosionEffect(position, radius));
    }

    private IEnumerator ExplosionEffect(Vector3 position, float radius)
    {
        GameObject vfx = new GameObject("Explosion_VFX");
        vfx.transform.position = position;
        vfx.transform.SetParent(vfxContainer);

        // Burst particles
        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, Color.red, 0.5f, 0.3f);
        var main = ps.main;
        main.startSpeed = 5f;

        var emission = ps.emission;
        emission.enabled = false;

        var burst = new ParticleSystem.Burst(0f, 30);
        emission.SetBurst(0, burst);

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;

        ps.Play();

        // Expanding ring
        GameObject ringObj = new GameObject("ExplosionRing");
        ringObj.transform.SetParent(vfx.transform);
        ringObj.transform.localPosition = Vector3.zero;

        LineRenderer ring = ringObj.AddComponent<LineRenderer>();
        ring.material = VFXHelper.CreateUnlitMaterial(Color.yellow);
        ring.startWidth = 0.1f;
        ring.endWidth = 0.1f;
        VFXHelper.SetupCircle(ring, 0.1f);

        float duration = 0.4f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            float currentRadius = Mathf.Lerp(0.1f, radius, t);
            VFXHelper.SetupCircle(ring, currentRadius);

            Color col = Color.Lerp(Color.yellow, Color.red, t);
            col.a = 1f - t;
            ring.startColor = col;
            ring.endColor = col;

            yield return null;
        }

        Destroy(vfx, 1f);
    }

    /// <summary>
    /// Piercing beam effect
    /// </summary>
    public GameObject CreateBeam(Vector3 start, Vector3 end, float duration = 0.3f)
    {
        GameObject vfx = new GameObject("Beam_VFX");
        vfx.transform.position = start;
        vfx.transform.SetParent(vfxContainer);

        LineRenderer beam = vfx.AddComponent<LineRenderer>();
        beam.material = VFXHelper.CreateUnlitMaterial(Color.cyan);
        beam.startWidth = 0.15f;
        beam.endWidth = 0.1f;
        beam.positionCount = 2;
        beam.SetPosition(0, start);
        beam.SetPosition(1, end);

        StartCoroutine(AnimateBeam(beam, duration));
        Destroy(vfx, duration + 0.1f);

        return vfx;
    }

    private IEnumerator AnimateBeam(LineRenderer beam, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            // Pulse width
            float width = Mathf.Lerp(0.15f, 0.05f, t);
            beam.startWidth = width;
            beam.endWidth = width * 0.7f;

            // Fade color
            Color col = Color.cyan;
            col.a = 1f - t;
            beam.startColor = col;
            beam.endColor = col;

            yield return null;
        }
    }

    /// <summary>
    /// Melee slash arc effect
    /// </summary>
    public GameObject CreateSlashArc(Transform attacker, bool facingRight)
    {
        GameObject vfx = new GameObject("SlashArc_VFX");
        vfx.transform.SetParent(attacker);
        vfx.transform.localPosition = new Vector3(facingRight ? 0.5f : -0.5f, 0, 0);
        vfx.transform.localRotation = Quaternion.identity;

        LineRenderer arc = vfx.AddComponent<LineRenderer>();
        arc.material = VFXHelper.CreateUnlitMaterial(Color.white);
        arc.startWidth = 0.2f;
        arc.endWidth = 0.05f;

        float startAngle = facingRight ? -45f : 135f;
        float endAngle = facingRight ? 45f : 225f;
        VFXHelper.SetupArc(arc, 1f, startAngle, endAngle, 20);

        StartCoroutine(AnimateSlash(arc, 0.2f));
        Destroy(vfx, 0.3f);

        return vfx;
    }

    private IEnumerator AnimateSlash(LineRenderer arc, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            Color col = Color.Lerp(Color.white, Color.gray, t);
            col.a = 1f - t;
            arc.startColor = col;
            arc.endColor = col;

            yield return null;
        }
    }

    /// <summary>
    /// Ground slam shockwave effect
    /// </summary>
    public void CreateShockwave(Vector3 position)
    {
        StartCoroutine(ShockwaveEffect(position));
    }

    private IEnumerator ShockwaveEffect(Vector3 position)
    {
        GameObject vfx = new GameObject("Shockwave_VFX");
        vfx.transform.position = position;
        vfx.transform.SetParent(vfxContainer);

        // Create 3 expanding rings with delay
        for (int i = 0; i < 3; i++)
        {
            GameObject ringObj = new GameObject($"Ring_{i}");
            ringObj.transform.SetParent(vfx.transform);
            ringObj.transform.localPosition = Vector3.zero;

            LineRenderer ring = ringObj.AddComponent<LineRenderer>();
            ring.material = VFXHelper.CreateUnlitMaterial(new Color(0.8f, 0.6f, 0.3f));
            ring.startWidth = 0.15f;
            ring.endWidth = 0.15f;
            VFXHelper.SetupCircle(ring, 0.1f);

            StartCoroutine(AnimateShockwaveRing(ring, 0.5f, 3f, i * 0.1f));
        }

        // Dust particles
        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, new Color(0.6f, 0.5f, 0.4f), 0.8f, 0.15f);
        var emission = ps.emission;
        emission.enabled = false;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, 20));

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;

        ps.Play();

        Destroy(vfx, 2f);
        yield return null;
    }

    private IEnumerator AnimateShockwaveRing(LineRenderer ring, float duration, float maxRadius, float delay)
    {
        yield return new WaitForSeconds(delay);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            float radius = Mathf.Lerp(0.1f, maxRadius, t);
            VFXHelper.SetupCircle(ring, radius);

            Color col = ring.startColor;
            col.a = 1f - t;
            ring.startColor = col;
            ring.endColor = col;

            yield return null;
        }
    }

    #endregion

    #region Movement Effects

    /// <summary>
    /// Dash speed lines and trail
    /// </summary>
    public GameObject CreateDashEffect(Transform player)
    {
        GameObject vfx = new GameObject("Dash_VFX");
        vfx.transform.SetParent(player);
        vfx.transform.localPosition = Vector3.zero;

        // Trail
        TrailRenderer trail = vfx.AddComponent<TrailRenderer>();
        trail.time = 0.3f;
        trail.startWidth = 0.5f;
        trail.endWidth = 0.05f;
        trail.material = VFXHelper.CreateUnlitMaterial(Color.cyan);
        trail.startColor = Color.cyan;
        trail.endColor = new Color(0, 1, 1, 0);

        Destroy(vfx, 1f);
        return vfx;
    }

    /// <summary>
    /// Multi-jump burst particles
    /// </summary>
    public void CreateJumpBurst(Vector3 position)
    {
        GameObject vfx = new GameObject("JumpBurst_VFX");
        vfx.transform.position = position;
        vfx.transform.SetParent(vfxContainer);

        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, Color.white, 0.4f, 0.15f);

        var main = ps.main;
        main.startSpeed = 3f;

        var emission = ps.emission;
        emission.enabled = false;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, 15));

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;

        ps.Play();
        Destroy(vfx, 1f);
    }

    /// <summary>
    /// Blink teleport fade effect
    /// </summary>
    public void CreateBlinkEffect(Vector3 startPos, Vector3 endPos, Transform player)
    {
        StartCoroutine(BlinkAnimation(startPos, endPos, player));
    }

    private IEnumerator BlinkAnimation(Vector3 startPos, Vector3 endPos, Transform player)
    {
        // Fade out at start
        CreateFadeRing(startPos, false);

        // Wait for blink
        yield return new WaitForSeconds(0.1f);

        // Fade in at end
        CreateFadeRing(endPos, true);
    }

    private void CreateFadeRing(Vector3 position, bool fadeIn)
    {
        GameObject vfx = new GameObject(fadeIn ? "BlinkIn_VFX" : "BlinkOut_VFX");
        vfx.transform.position = position;
        vfx.transform.SetParent(vfxContainer);

        LineRenderer ring = vfx.AddComponent<LineRenderer>();
        ring.material = VFXHelper.CreateUnlitMaterial(Color.cyan);
        ring.startWidth = 0.1f;
        ring.endWidth = 0.1f;
        VFXHelper.SetupCircle(ring, 0.5f);

        StartCoroutine(AnimateFadeRing(ring, 0.3f, fadeIn));
        Destroy(vfx, 0.4f);
    }

    private IEnumerator AnimateFadeRing(LineRenderer ring, float duration, bool fadeIn)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;

            float alpha = fadeIn ? t : (1f - t);
            Color col = Color.cyan;
            col.a = alpha;
            ring.startColor = col;
            ring.endColor = col;

            yield return null;
        }
    }

    #endregion

    #region Defense Effects

    /// <summary>
    /// Shield buff rotating ring
    /// </summary>
    public GameObject CreateShieldRing(Transform player, float duration)
    {
        GameObject vfx = new GameObject("Shield_VFX");
        vfx.transform.SetParent(player);
        vfx.transform.localPosition = Vector3.zero;

        LineRenderer ring = vfx.AddComponent<LineRenderer>();
        ring.material = VFXHelper.CreateUnlitMaterial(Color.blue);
        ring.startWidth = 0.1f;
        ring.endWidth = 0.1f;
        VFXHelper.SetupCircle(ring, 1.2f);

        StartCoroutine(AnimateShieldRing(ring, duration));
        Destroy(vfx, duration);

        return vfx;
    }

    private IEnumerator AnimateShieldRing(LineRenderer ring, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Rotate
            ring.transform.Rotate(0, 0, 180f * Time.deltaTime);

            // Pulse
            float pulse = Mathf.Sin(elapsed * 5f) * 0.5f + 0.5f;
            Color col = Color.Lerp(Color.blue, Color.cyan, pulse);
            col.a = 0.7f;
            ring.startColor = col;
            ring.endColor = col;

            yield return null;
        }
    }

    /// <summary>
    /// Instant heal sparkles
    /// </summary>
    public void CreateHealEffect(Vector3 position)
    {
        GameObject vfx = new GameObject("Heal_VFX");
        vfx.transform.position = position;
        vfx.transform.SetParent(vfxContainer);

        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, Color.green, 1f, 0.1f);

        var main = ps.main;
        main.startSpeed = 2f;
        main.gravityModifier = -0.5f; // Float upward

        var emission = ps.emission;
        emission.enabled = false;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, 20));

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;

        ps.Play();
        Destroy(vfx, 1.5f);
    }

    /// <summary>
    /// Invulnerability flash effect
    /// </summary>
    public GameObject CreateInvulnerabilityFlash(Transform player, float duration)
    {
        GameObject vfx = new GameObject("Invulnerability_VFX");
        vfx.transform.SetParent(player);
        vfx.transform.localPosition = Vector3.zero;

        // Add sprite for flash overlay
        SpriteRenderer flash = vfx.AddComponent<SpriteRenderer>();
        flash.color = new Color(1, 1, 1, 0.3f);
        flash.sortingOrder = 100;

        // Create simple circle sprite programmatically
        Texture2D tex = new Texture2D(32, 32);
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), Vector2.one * 16) / 16f;
                Color col = dist < 1f ? Color.white : Color.clear;
                tex.SetPixel(x, y, col);
            }
        }
        tex.Apply();
        flash.sprite = Sprite.Create(tex, new Rect(0, 0, 32, 32), Vector2.one * 0.5f, 32);

        StartCoroutine(AnimateInvulnerability(flash, duration));
        Destroy(vfx, duration);

        return vfx;
    }

    private IEnumerator AnimateInvulnerability(SpriteRenderer flash, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Pulse
            float pulse = Mathf.Sin(elapsed * 10f) * 0.5f + 0.5f;
            Color col = flash.color;
            col.a = pulse * 0.5f;
            flash.color = col;

            yield return null;
        }
    }

    /// <summary>
    /// Damage reduction hexagonal barrier
    /// </summary>
    public GameObject CreateBarrierHexagon(Transform player, float duration)
    {
        GameObject vfx = new GameObject("Barrier_VFX");
        vfx.transform.SetParent(player);
        vfx.transform.localPosition = Vector3.zero;

        LineRenderer hex = vfx.AddComponent<LineRenderer>();
        hex.material = VFXHelper.CreateUnlitMaterial(Color.yellow);
        hex.startWidth = 0.08f;
        hex.endWidth = 0.08f;
        VFXHelper.SetupHexagon(hex, 1f);

        StartCoroutine(AnimateBarrier(hex, duration));
        Destroy(vfx, duration);

        return vfx;
    }

    private IEnumerator AnimateBarrier(LineRenderer hex, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Orbit
            hex.transform.Rotate(0, 0, 90f * Time.deltaTime);

            // Pulse color
            float pulse = Mathf.Sin(elapsed * 4f) * 0.5f + 0.5f;
            Color col = Color.Lerp(Color.yellow, new Color(1f, 0.5f, 0f), pulse);
            hex.startColor = col;
            hex.endColor = col;

            yield return null;
        }
    }

    #endregion

    #region Utility Effects

    /// <summary>
    /// Stun stars circling head
    /// </summary>
    public GameObject CreateStunStars(Transform target, float duration)
    {
        GameObject vfx = new GameObject("Stun_VFX");
        vfx.transform.SetParent(target);
        vfx.transform.localPosition = new Vector3(0, 1.5f, 0);

        // Create 3 stars
        for (int i = 0; i < 3; i++)
        {
            GameObject starObj = new GameObject($"Star_{i}");
            starObj.transform.SetParent(vfx.transform);
            starObj.transform.localPosition = Vector3.zero;

            LineRenderer star = starObj.AddComponent<LineRenderer>();
            star.material = VFXHelper.CreateUnlitMaterial(Color.yellow);
            star.startWidth = 0.05f;
            star.endWidth = 0.05f;
            VFXHelper.SetupStar(star, 0.2f, 0.08f);

            StartCoroutine(AnimateStunStar(starObj.transform, duration, i * 120f));
        }

        Destroy(vfx, duration);
        return vfx;
    }

    private IEnumerator AnimateStunStar(Transform star, float duration, float angleOffset)
    {
        float elapsed = 0f;
        float orbitRadius = 0.5f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float angle = (elapsed * 180f + angleOffset) * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * orbitRadius;
            float y = Mathf.Sin(angle) * orbitRadius;
            star.localPosition = new Vector3(x, y, 0);

            // Wobble rotation
            star.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 5f) * 20f);

            yield return null;
        }
    }

    /// <summary>
    /// Slow ice particles
    /// </summary>
    public GameObject CreateSlowEffect(Transform target, float duration)
    {
        GameObject vfx = new GameObject("Slow_VFX");
        vfx.transform.SetParent(target);
        vfx.transform.localPosition = Vector3.zero;

        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, new Color(0.5f, 0.8f, 1f), 2f, 0.08f);

        var main = ps.main;
        main.startSpeed = 0.5f;
        main.gravityModifier = 0.2f;

        var emission = ps.emission;
        emission.rateOverTime = 10;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.8f;

        Destroy(vfx, duration);
        return vfx;
    }

    /// <summary>
    /// Airborne upward wind swirl
    /// </summary>
    public void CreateAirborneEffect(Vector3 position, float height)
    {
        GameObject vfx = new GameObject("Airborne_VFX");
        vfx.transform.position = position;
        vfx.transform.SetParent(vfxContainer);

        ParticleSystem ps = VFXHelper.CreateBasicParticles(vfx, Color.white, 1f, 0.1f);

        var main = ps.main;
        main.startSpeed = 4f;
        main.gravityModifier = -0.3f;

        var emission = ps.emission;
        emission.rateOverTime = 20;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.2f;

        var velocityOverLifetime = ps.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
        velocityOverLifetime.orbitalX = 2f; // Spiral effect

        Destroy(vfx, 1f);
    }

    #endregion
}
