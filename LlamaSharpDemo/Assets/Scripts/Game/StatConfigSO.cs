using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StatConfig", menuName = "Game/Stat Config")]
public class StatConfigSO : ScriptableObject
{
    [Serializable]
    public class StatDefinition
    {
        public float BaseValue;
        public float MinValue;
        public float MaxValue;
    }

    public StatDefinition AttackPower = new() { BaseValue = 50f, MinValue = 10f, MaxValue = 100f };
    public StatDefinition AttackSpeed = new() { BaseValue = 5f, MinValue = 1f, MaxValue = 20f };
    public StatDefinition CooldownHaste = new() { BaseValue = 0f, MinValue = 0f, MaxValue = 50f };
    public StatDefinition Defense = new() { BaseValue = 30f, MinValue = 0f, MaxValue = 100f };
    public StatDefinition JumpPower = new() { BaseValue = 10f, MinValue = 5f, MaxValue = 20f };
    public StatDefinition MaxHealth = new() { BaseValue = 200f, MinValue = 100f, MaxValue = 1000f };
    public StatDefinition MovementSpeed = new() { BaseValue = 10f, MinValue = 5f, MaxValue = 20f };
    public StatDefinition ProjectileRange = new() { BaseValue = 100f, MinValue = 50f, MaxValue = 200f };

    [TextArea(3, 10)]
    public string CharacterDescription = "A brave warrior.";

    public Dictionary<string, StatDefinition> GetStats()
    {
        return new Dictionary<string, StatDefinition>
        {
            { "AttackPower", AttackPower },
            { "AttackSpeed", AttackSpeed },
            { "CooldownHaste", CooldownHaste },
            { "Defense", Defense },
            { "JumpPower", JumpPower },
            { "MaxHealth", MaxHealth },
            { "MovementSpeed", MovementSpeed },
            { "ProjectileRange", ProjectileRange }
        };
    }
}
