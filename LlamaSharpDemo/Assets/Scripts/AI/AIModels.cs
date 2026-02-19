using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class StatChanges
{
    public float Speed;
    public float Attack;
    public float Defense;
    public float Jump;
    public float Attack_Speed;
    public float Range;
    public float CooldownHaste;
    public float MaxHP;
}

[Serializable]
public class StatModel
{
    public StatChanges stat_changes;
    public float duration_seconds;
}

[Serializable]
public class SkillData
{
    public string name;
    public List<string> sequence;
    public string description;
    public float cooldown;
    public float duration;
}

[Serializable]
public class SkillModel
{
    public List<SkillData> new_skills;
}

[Serializable]
public class AIResponse
{
    public StatModel stat_model;
    public SkillModel skill_model;
}

public class RuntimeSkill
{
    public SkillData skillData;
    public float lastUsedTime;
    public bool isReady = true;

    public RuntimeSkill(SkillData data)
    {
        skillData = data;
        lastUsedTime = -999f;
    }

    public bool CanUse()
    {
        return isReady && (Time.time - lastUsedTime >= skillData.cooldown);
    }

    public void Use()
    {
        lastUsedTime = Time.time;
        isReady = false;
    }

    public void Reset()
    {
        isReady = true;
    }
}