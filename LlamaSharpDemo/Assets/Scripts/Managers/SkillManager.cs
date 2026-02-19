using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SkillManager : MonoBehaviour
{
    [Header("Components")]
    public SkillExecutor skillExecutor;

    [Header("UI")]
    public SkillNotification skillNotification;

    [Header("Active Skills")]
    public List<RuntimeSkill> activeSkills = new List<RuntimeSkill>();

    [Header("Key Bindings")]
    public KeyCode skill1Key = KeyCode.Q;
    public KeyCode skill2Key = KeyCode.E;

    void Update()
    {
        if (DemoInput.GetKeyDown(skill1Key) && activeSkills.Count > 0)
        {
            TryUseSkill(0);
        }

        if (DemoInput.GetKeyDown(skill2Key) && activeSkills.Count > 1)
        {
            TryUseSkill(1);
        }
    }

    public void AddSkill(SkillData skillData)
    {
        RuntimeSkill newSkill = new RuntimeSkill(skillData);
        activeSkills.Add(newSkill);

        Debug.Log($"[SkillManager] Added skill: {skillData.name}");
        Debug.Log($"  Total skills: {activeSkills.Count}");
        Debug.Log($"  Press {skill1Key} to use skill 1, {skill2Key} to use skill 2");
    }

    public void ClearSkills()
    {
        activeSkills.Clear();
        Debug.Log("[SkillManager] All skills cleared");
    }

    void TryUseSkill(int index)
    {
        if (index >= activeSkills.Count) return;

        RuntimeSkill skill = activeSkills[index];

        if (!skill.CanUse())
        {
            float remainingCooldown = skill.skillData.cooldown - (Time.time - skill.lastUsedTime);
            Debug.Log($"Skill on cooldown! {remainingCooldown:F1}s remaining");
            return;
        }

        skill.Use();
        StartCoroutine(ExecuteSkillRoutine(skill));
    }

    IEnumerator ExecuteSkillRoutine(RuntimeSkill skill)
    {
        Debug.Log($"Using skill: {skill.skillData.name}");

        if (skillNotification != null)
        {
            skillNotification.ShowSkill(skill.skillData.name, skill.skillData.description);
        }

        yield return skillExecutor.ExecuteSkill(skill.skillData);

        yield return new WaitForSeconds(skill.skillData.cooldown);
        skill.Reset();
    }
}
