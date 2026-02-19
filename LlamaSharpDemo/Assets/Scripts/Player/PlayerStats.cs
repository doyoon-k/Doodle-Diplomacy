using System.Collections.Generic;
using UnityEngine;

public class PlayerStats : MonoBehaviour
{
    [Header("Configuration")]
    public StatConfigSO statConfig;

    [Header("Current Stats (Runtime)")]
    private Dictionary<string, float> _currentStats = new Dictionary<string, float>();
    public float CurrentHealth { get; set; }

    [Header("Stat Change Tracking")]
    private Dictionary<string, float> _previousStats = new Dictionary<string, float>();
    public Dictionary<string, float> StatDeltas { get; private set; } = new Dictionary<string, float>();

    [TextArea] public string characterDescription = "A brave warrior.";

    [Header("Death Settings")]
    public bool isDead = false;

    [Header("UI")]
    public UnityEngine.UI.Slider hpBar;
    public GameObject damagePopupPrefab;
    public Transform popupSpawnPoint;

    void Start()
    {
        InitializeStats();

        if (hpBar != null)
        {
            hpBar.maxValue = GetStat("MaxHealth");
            hpBar.value = CurrentHealth;
        }

        Debug.Log($"PlayerStats initialized - HP: {CurrentHealth}, Attack: {GetStat("AttackPower")}");
    }

    public void InitializeStats()
    {
        _currentStats.Clear();

        if (statConfig != null)
        {
            foreach (var kvp in statConfig.GetStats())
            {
                _currentStats[kvp.Key] = kvp.Value.BaseValue;
            }

            if (!string.IsNullOrEmpty(statConfig.CharacterDescription))
            {
                characterDescription = statConfig.CharacterDescription;
            }
        }

        CurrentHealth = GetStat("MaxHealth");
        isDead = false;
    }

    public float GetStat(string statName)
    {
        if (_currentStats.TryGetValue(statName, out float value))
        {
            return value;
        }
        // Debug.LogWarning($"Stat {statName} not found!"); // Optional: suppress warning if frequent
        return 0f;
    }

    public void SetStat(string statName, float value)
    {
        if (_currentStats.ContainsKey(statName))
        {
            _currentStats[statName] = value;
        }
        else
        {
            _currentStats.Add(statName, value);
        }
    }

    public void ModifyStat(string statName, float amount)
    {
        if (_currentStats.ContainsKey(statName))
        {
            _currentStats[statName] += amount;
        }
        else
        {
            _currentStats.Add(statName, amount);
        }

        // Clamp values if needed (e.g. non-negative)
        // For now, simple addition
    }

    public void ResetToBaseStats()
    {
        Debug.Log("Resetting stats to base values");
        InitializeStats();
        ClearStatDeltas(); // Clear stat change indicators
        LogCurrentStats();
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        SkillExecutor skillExecutor = GetComponent<SkillExecutor>();
        if (skillExecutor != null)
        {
            damage = skillExecutor.ProcessIncomingDamage(damage);
            if (damage <= 0f) return;
        }

        float defense = GetStat("Defense");
        float actualDamage = Mathf.Max(0, damage - defense);
        CurrentHealth -= actualDamage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);

        if (hpBar != null)
        {
            hpBar.value = CurrentHealth;
        }

        if (damagePopupPrefab != null && popupSpawnPoint != null)
        {
            Vector3 spawnPos = popupSpawnPoint.position;
            GameObject popup = Instantiate(damagePopupPrefab, spawnPos, Quaternion.identity, GameObject.Find("Canvas").transform);
            DamagePopup popupScript = popup.GetComponent<DamagePopup>();
            if (popupScript != null)
            {
                popupScript.Initialize(actualDamage, actualDamage > 50);
            }
        }

        Debug.Log($"Took {actualDamage} damage! HP: {CurrentHealth}/{GetStat("MaxHealth")}");

        if (CurrentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(float amount)
    {
        if (isDead) return;

        CurrentHealth += amount;
        CurrentHealth = Mathf.Min(CurrentHealth, GetStat("MaxHealth"));

        if (hpBar != null)
        {
            hpBar.value = CurrentHealth;
        }

        Debug.Log($"Healed {amount}! HP: {CurrentHealth}/{GetStat("MaxHealth")}");
    }

    void Die()
    {
        isDead = true;
        Debug.Log($"[DEATH] {gameObject.name} has died!");

        GetComponent<SpriteRenderer>().color = Color.gray;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Static;
        }

        MonoBehaviour[] scripts = GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour script in scripts)
        {
            if (script != this)
            {
                script.enabled = false;
            }
        }
    }

    public void Revive()
    {
        isDead = false;
        CurrentHealth = GetStat("MaxHealth");
        Debug.Log($"[REVIVE] {gameObject.name} has revived!");

        GetComponent<SpriteRenderer>().color = Color.white;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
        }

        MonoBehaviour[] scripts = GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour script in scripts)
        {
            script.enabled = true;
        }

        if (hpBar != null)
        {
            hpBar.value = CurrentHealth;
        }
    }

    void LogCurrentStats()
    {
        string log = "Current Stats: ";
        foreach (var kvp in _currentStats)
        {
            log += $"{kvp.Key}: {kvp.Value}, ";
        }
        log += $"HP: {CurrentHealth}/{GetStat("MaxHealth")}";
        Debug.Log(log);
    }

    /// <summary>
    /// Snapshot current stats before applying changes. Call this before modifying stats.
    /// </summary>
    public void SnapshotStats()
    {
        _previousStats.Clear();
        foreach (var kvp in _currentStats)
        {
            _previousStats[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Calculate deltas after stats have been modified. Call this after applying changes.
    /// </summary>
    public void CalculateStatDeltas()
    {
        StatDeltas.Clear();
        foreach (var kvp in _currentStats)
        {
            float previous = _previousStats.ContainsKey(kvp.Key) ? _previousStats[kvp.Key] : kvp.Value;
            StatDeltas[kvp.Key] = kvp.Value - previous;
        }
    }

    /// <summary>
    /// Get the delta (change) for a specific stat.
    /// </summary>
    public float GetStatDelta(string statName)
    {
        return StatDeltas.TryGetValue(statName, out float delta) ? delta : 0f;
    }

    /// <summary>
    /// Clear all stat deltas. Typically called after a delay to hide change indicators.
    /// </summary>
    public void ClearStatDeltas()
    {
        StatDeltas.Clear();
    }
}