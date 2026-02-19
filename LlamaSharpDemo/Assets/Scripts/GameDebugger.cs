using UnityEngine;

public class GameDebugger : MonoBehaviour
{
    [Header("Targets")]
    public PlayerStats playerStats;
    public PlayerStats enemyStats;

    [Header("Debug Settings")]
    public float testDamage = 50f; // Set higher than Defense (30) to ensure damage
    public float testHeal = 50f;

    void Update()
    {
        // Key 1: Heal Player
        if (DemoInput.GetKeyDown(KeyCode.Alpha1))
        {
            if (playerStats != null)
            {
                Debug.Log($"=== [Debug] 1: Heal Player ({testHeal}) ===");
                playerStats.Heal(testHeal);
            }
            else
            {
                Debug.LogWarning("Player stats not assigned in GameDebugger!");
            }
        }

        // Key 2: Damage Player (Self Harm)
        if (DemoInput.GetKeyDown(KeyCode.Alpha2))
        {
            if (playerStats != null)
            {
                Debug.Log($"=== [Debug] 2: Damage Player ({testDamage}) ===");
                // Damage calculation in PlayerStats handles defense subtraction
                playerStats.TakeDamage(testDamage);
            }
        }

        // Key 3: Heal Enemy (New Request)
        if (DemoInput.GetKeyDown(KeyCode.Alpha3))
        {
            if (enemyStats != null)
            {
                Debug.Log($"=== [Debug] 3: Heal Enemy ({testHeal}) ===");
                enemyStats.Heal(testHeal);
            }
            else
            {
                Debug.LogWarning("Enemy stats not assigned in GameDebugger!");
            }
        }
    }
}
