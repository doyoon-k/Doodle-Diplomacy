using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

public class ItemManager : MonoBehaviour
{
    public static ItemManager Instance;

    [Header("Configuration")]
    public PromptPipelineAsset pipelineAsset;

    [Header("Components")]
    public PlayerStats playerStats;
    public SkillManager skillManager;
    public PlayerController playerController;

    [Header("Inventory")]
    public List<ItemData> inventory = new List<ItemData>();
    public int currentEquipIndex = 0;
    public ItemData currentItem;

    [Header("Runtime State")]
    private GameObject currentStatusPopup;
    private Coroutine currentApplyItemCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        Debug.Log("ItemManager initialized!");
        Debug.Log("Controls: [T] Swap Item, [4] Use Item");

        if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
    }

    void Update()
    {
        if (playerController != null && !playerController.IsInputEnabled) return;

        // Debug Keys
        if (DemoInput.GetKeyDown(KeyCode.R))
        {
            if (playerStats != null) playerStats.ResetToBaseStats();
            if (skillManager != null) skillManager.ClearSkills();
        }

        if (DemoInput.GetKeyDown(KeyCode.E))
        {
            EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
            foreach (var enemy in enemies)
            {
                enemy.Respawn();
            }
        }

        if (DemoInput.GetKeyDown(KeyCode.T))
        {
            SwapToNextItem();
        }

        if (DemoInput.GetKeyDown(KeyCode.Alpha4))
        {
            if (currentItem != null)
            {
                if (pipelineAsset == null) { Debug.LogError("Pipeline Asset missing!"); return; }

                // Stop previous generation and cleanup
                GamePipelineRunner.Instance.StopGeneration();
                if (currentApplyItemCoroutine != null) StopCoroutine(currentApplyItemCoroutine);
                if (currentStatusPopup != null) Destroy(currentStatusPopup);

                currentApplyItemCoroutine = StartCoroutine(ApplyItem(currentItem));
            }
            else
            {
                Debug.LogWarning("No item equipped!");
            }
        }
    }

    public void SwapToNextItem()
    {
        if (inventory.Count == 0) return;

        currentEquipIndex = (currentEquipIndex + 1) % inventory.Count;
        currentItem = inventory[currentEquipIndex];

        Debug.Log($"Swapped to: {currentItem.itemName}");
    }

    public void AddItem(ItemData newItem)
    {
        inventory.Add(newItem);

        if (inventory.Count == 1)
        {
            currentEquipIndex = 0;
            currentItem = newItem;
        }

        Debug.Log($"Picked up {newItem.itemName}. Total items: {inventory.Count}");
    }

    IEnumerator ApplyItem(ItemData item)
    {
        Debug.Log($"=== Using Item: {item.itemName} ===");

        // Display Floating Status (Use class field)
        DamagePopup statusScript = null;

        if (playerStats != null && playerStats.damagePopupPrefab != null && playerStats.popupSpawnPoint != null)
        {
            // Find Canvas like PlayerStats does
            Transform canvasTransform = GameObject.Find("Canvas")?.transform;
            if (canvasTransform != null)
            {
                currentStatusPopup = Instantiate(playerStats.damagePopupPrefab, playerStats.popupSpawnPoint.position, Quaternion.identity, canvasTransform);
                statusScript = currentStatusPopup.GetComponent<DamagePopup>();
                if (statusScript != null)
                {
                    statusScript.InitializeStatus(playerStats.transform, $"Using {item.itemName}...");
                }
            }
            else
            {
                Debug.LogError("[ItemManager] Canvas not found!");
            }
        }
        else
        {
            Debug.LogWarning($"[ItemManager] Cannot spawn popup. Stats: {playerStats != null}, Prefab: {playerStats?.damagePopupPrefab != null}, SpawnPoint: {playerStats?.popupSpawnPoint != null}");
        }

        // 1. Initialize State
        Dictionary<string, string> state = new Dictionary<string, string>();
        state["item_name"] = item.itemName;
        state["item_description"] = item.description;

        // Real Stats
        if (playerStats != null)
        {
            if (playerStats.statConfig != null)
            {
                foreach (var kvp in playerStats.statConfig.GetStats())
                {
                    state[kvp.Key] = playerStats.GetStat(kvp.Key).ToString();
                }
            }
            else
            {
                // Fallback
                state["AttackPower"] = playerStats.GetStat("AttackPower").ToString();
                state["AttackSpeed"] = playerStats.GetStat("AttackSpeed").ToString();
                state["ProjectileRange"] = playerStats.GetStat("ProjectileRange").ToString();
                state["MovementSpeed"] = playerStats.GetStat("MovementSpeed").ToString();
                state["MaxHealth"] = playerStats.GetStat("MaxHealth").ToString();
                state["Defense"] = playerStats.GetStat("Defense").ToString();
                state["JumpPower"] = playerStats.GetStat("JumpPower").ToString();
                state["CooldownHaste"] = playerStats.GetStat("CooldownHaste").ToString();
            }
        }
        else
        {
            // Fallback if playerStats is missing
            state["AttackPower"] = "50";
            state["AttackSpeed"] = "1.0";
            state["ProjectileRange"] = "10";
            state["MovementSpeed"] = "5";
            state["MaxHealth"] = "100";
            state["Defense"] = "10";
            state["JumpPower"] = "10";
            state["CooldownHaste"] = "0";
        }

        if (playerStats != null)
        {
            state["character_description"] = playerStats.characterDescription;
        }
        else
        {
            state["character_description"] = "A brave warrior.";
        }

        bool pipelineFinished = false;

        GamePipelineRunner.Instance.RunPipeline(pipelineAsset, state, (finalState) =>
        {
            if (statusScript != null) statusScript.SetText("Evolving...");

            if (finalState == null)
            {
                if (currentStatusPopup != null) Destroy(currentStatusPopup);
                currentApplyItemCoroutine = null;
                return;
            }

            AIResponse response = MapStateToResponse(finalState);

            if (response.stat_model != null) ApplyStatModel(response.stat_model);
            if (response.skill_model != null) ApplySkillModel(response.skill_model);

            pipelineFinished = true;
        });

        while (!pipelineFinished) yield return null;

        if (currentStatusPopup != null) Destroy(currentStatusPopup);
        currentApplyItemCoroutine = null;
    }

    void ApplyStatModel(StatModel statModel)
    {
        if (statModel == null || statModel.stat_changes == null) return;

        if (playerStats == null) return;

        // Snapshot stats before applying changes
        playerStats.SnapshotStats();

        // Map StatChanges to ModifyStat calls
        if (statModel.stat_changes.Speed != 0) playerStats.ModifyStat("MovementSpeed", statModel.stat_changes.Speed);
        if (statModel.stat_changes.Attack != 0) playerStats.ModifyStat("AttackPower", statModel.stat_changes.Attack);
        if (statModel.stat_changes.Defense != 0) playerStats.ModifyStat("Defense", statModel.stat_changes.Defense);
        if (statModel.stat_changes.Jump != 0) playerStats.ModifyStat("JumpPower", statModel.stat_changes.Jump);
        if (statModel.stat_changes.Attack_Speed != 0) playerStats.ModifyStat("AttackSpeed", statModel.stat_changes.Attack_Speed);
        if (statModel.stat_changes.Range != 0) playerStats.ModifyStat("ProjectileRange", statModel.stat_changes.Range);
        if (statModel.stat_changes.CooldownHaste != 0) playerStats.ModifyStat("CooldownHaste", statModel.stat_changes.CooldownHaste);
        if (statModel.stat_changes.MaxHP != 0) playerStats.ModifyStat("MaxHealth", statModel.stat_changes.MaxHP);

        // Calculate deltas after applying changes
        playerStats.CalculateStatDeltas();

        // Deltas now persist until stats are reset with 'R' key
    }

    void ApplySkillModel(SkillModel skillModel)
    {
        if (skillModel == null || skillModel.new_skills == null) return;
        if (skillManager != null)
        {
            skillManager.ClearSkills();
            foreach (var skill in skillModel.new_skills) skillManager.AddSkill(skill);
        }
    }

    // --- Mapping Logic ---

    private AIResponse MapStateToResponse(Dictionary<string, string> state)
    {
        AIResponse response = new AIResponse();
        response.stat_model = new StatModel { stat_changes = new StatChanges() };
        response.skill_model = new SkillModel { new_skills = new List<SkillData>() };

        try
        {
            // --- Stat Mapping ---
            if (state.ContainsKey("stat") && state.ContainsKey("value"))
                ApplySingleStat(response.stat_model.stat_changes, state["stat"], state["value"]);

            string nestedJson = GetValue(state, "statChanges", "stat_changes", "stats");
            if (!string.IsNullOrEmpty(nestedJson)) ParseNestedStats(nestedJson, response.stat_model.stat_changes);

            // Set permanent duration
            response.stat_model.duration_seconds = 5.0f;

            // --- Description Mapping ---
            string newDesc = GetValue(state, "newDescription", "new_description", "description", "characterDescription", "character_description");
            if (!string.IsNullOrEmpty(newDesc) && playerStats != null)
            {
                playerStats.characterDescription = newDesc;
            }

            // --- Skill Mapping ---
            SkillData skillData = new SkillData();
            bool skillFound = false;

            // 1. Try to parse "NewSkill" as a JSON object first
            string newSkillJson = GetValue(state, "NewSkill", "new_skill");
            if (!string.IsNullOrEmpty(newSkillJson))
            {
                try
                {
                    if (TryParseJsonElement(newSkillJson, out JsonElement skillObj) &&
                        skillObj.ValueKind == JsonValueKind.Object)
                    {
                        // Extract Name
                        if (TryGetStringProperty(skillObj, "abilityName", out string abilityName))
                        {
                            skillData.name = abilityName;
                        }
                        else if (TryGetStringProperty(skillObj, "skillName", out string skillName))
                        {
                            skillData.name = skillName;
                        }
                        else
                        {
                            skillData.name = "Unknown Skill";
                        }

                        // Extract Description
                        if (TryGetStringProperty(skillObj, "description", out string description))
                        {
                            skillData.description = description;
                        }
                        else if (TryGetStringProperty(skillObj, "abilityDescription", out string abilityDescription))
                        {
                            skillData.description = abilityDescription;
                        }
                        else
                        {
                            skillData.description = "Generated by AI";
                        }

                        // Extract Primitives
                        skillData.sequence = new List<string>();
                        if (skillObj.TryGetProperty("primitiveActions", out JsonElement primitives) ||
                            skillObj.TryGetProperty("primitives", out primitives))
                        {
                            AppendPrimitiveSequence(primitives, skillData.sequence);
                        }

                        if (skillData.sequence.Count == 0)
                        {
                            skillData.sequence.Add("Attack");
                        }

                        skillData.cooldown = 3.0f;
                        skillFound = true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ItemManager] Failed to parse NewSkill JSON: {e.Message}");
                }
            }

            // 2. Fallback to flat keys if NewSkill wasn't valid or found
            if (!skillFound)
            {
                string nameVal = GetValue(state, "Name", "abilityName", "ability_name", "skillName", "skill_name");
                string primitivesJson = GetValue(state, "Primitives", "primitives", "primitiveActions", "primitive_actions");

                if (!string.IsNullOrEmpty(nameVal))
                {
                    skillData.name = nameVal;
                    skillFound = true;
                }
                else if (!string.IsNullOrEmpty(primitivesJson))
                {
                    skillData.name = "Unknown Power";
                    skillFound = true;
                }

                if (skillFound)
                {
                    string descVal = GetValue(state, "Description", "description", "abilityDescription", "ability_description", "flavor", "Flavor");
                    skillData.description = !string.IsNullOrEmpty(descVal) ? descVal : "Generated by AI";
                    skillData.cooldown = 3.0f;

                    skillData.sequence = new List<string>();

                    if (!string.IsNullOrEmpty(primitivesJson))
                    {
                        try
                        {
                            if (TryParseJsonElement(primitivesJson, out JsonElement token))
                            {
                                skillData.sequence.Clear();
                                AppendPrimitiveSequence(token, skillData.sequence);
                            }
                        }
                        catch
                        {
                            // Ignore malformed primitive JSON.
                        }

                        if (skillData.sequence.Count == 0)
                        {
                            skillData.sequence.Add("Attack");
                        }
                    }
                    else
                    {
                        skillData.sequence.Add("Attack");
                    }
                }
            }

            if (skillFound)
            {
                response.skill_model.new_skills.Add(skillData);
                Debug.Log($"[ItemManager] Mapped skill: {skillData.name}");
            }
            else
            {
                Debug.LogWarning("[ItemManager] AI returned empty skill. Creating fallback.");
                SkillData fallbackSkill = new SkillData
                {
                    name = "Fizzled Magic",
                    description = "The item's power flickered out. (AI Error)",
                    cooldown = 1.0f,
                    sequence = new List<string> { "Attack" }
                };
                response.skill_model.new_skills.Add(fallbackSkill);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ItemManager] Mapping Error: {e.Message}");
        }

        return response;
    }

    private static bool TryParseJsonElement(string json, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            element = document.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetStringProperty(JsonElement obj, string name, out string value)
    {
        value = null;
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out JsonElement prop))
        {
            return false;
        }

        value = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Null => null,
            _ => prop.ToString()
        };
        return !string.IsNullOrEmpty(value);
    }

    private static void AppendPrimitiveSequence(JsonElement token, List<string> output)
    {
        if (output == null)
        {
            return;
        }

        if (token.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in token.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                {
                    string primitive = item.GetString();
                    if (!string.IsNullOrWhiteSpace(primitive))
                    {
                        output.Add(primitive);
                    }
                    break;
                }
                case JsonValueKind.Object:
                                {
                    if (item.TryGetProperty("primitiveId", out JsonElement primitiveId))
                    {
                        string primitive = primitiveId.ValueKind == JsonValueKind.String
                            ? primitiveId.GetString()
                            : primitiveId.ToString();
                        if (!string.IsNullOrWhiteSpace(primitive))
                        {
                            output.Add(primitive);
                        }
                    }
                    break;
                }
            }
        }
    }

    private void ApplySingleStat(StatChanges stats, string statName, string valueStr, string changeType = "additive")
    {
        float value = ParseFloat(valueStr);
        float delta = value;

        string key = statName.ToLower().Replace(" ", "");

        // Identify which stat we are targeting
        bool isAttackSpeed = key.Contains("attackspeed") || (key.Contains("attack") && key.Contains("speed"));
        bool isAttackPower = !isAttackSpeed && (key.Contains("attack") || key.Contains("power")); // AttackPower or Attack
        bool isMovementSpeed = !isAttackSpeed && (key.Contains("speed") || key.Contains("move")); // MovementSpeed or Speed
        bool isRange = key.Contains("range") || key.Contains("projectile");
        bool isDefense = key.Contains("defense");
        bool isJump = key.Contains("jump");
        bool isHaste = key.Contains("cooldown") || key.Contains("haste");
        bool isHealth = key.Contains("health") || key.Contains("hp");

        // Handle Multiplicative
        if (changeType.ToLower() == "multiplicative" && playerStats != null)
        {
            float currentVal = 0f;

            if (isAttackSpeed) currentVal = playerStats.GetStat("AttackSpeed");
            else if (isAttackPower) currentVal = playerStats.GetStat("AttackPower");
            else if (isMovementSpeed) currentVal = playerStats.GetStat("MovementSpeed");
            else if (isRange) currentVal = playerStats.GetStat("ProjectileRange");
            else if (isDefense) currentVal = playerStats.GetStat("Defense");
            else if (isJump) currentVal = playerStats.GetStat("JumpPower");
            else if (isHaste) currentVal = playerStats.GetStat("CooldownHaste");
            else if (isHealth) currentVal = playerStats.GetStat("MaxHealth");

            delta = currentVal * (value - 1f);
        }

        // Apply Delta
        if (isAttackSpeed) stats.Attack_Speed = delta;
        else if (isAttackPower) stats.Attack = delta;
        else if (isMovementSpeed) stats.Speed = delta;
        else if (isRange) stats.Range = delta;
        else if (isDefense) stats.Defense = delta;
        else if (isJump) stats.Jump = delta;
        else if (isHaste) stats.CooldownHaste = delta;
        else if (isHealth) stats.MaxHP = delta;
    }

    private void ParseNestedStats(string json, StatChanges stats)
    {
        try
        {
            if (!TryParseJsonElement(json, out JsonElement token))
            {
                return;
            }

            if (token.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in token.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!TryGetStringProperty(item, "stat", out string s) ||
                        !TryGetStringProperty(item, "value", out string v))
                    {
                        continue;
                    }

                    string t = TryGetStringProperty(item, "changeType", out string parsedChangeType)
                        ? parsedChangeType
                        : "additive";
                    ApplySingleStat(stats, s, v, t);
                }
            }
            else if (token.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in token.EnumerateObject())
                {
                    ApplySingleStat(stats, prop.Name, prop.Value.ToString());
                }
            }
        }
        catch (Exception e) { Debug.LogWarning($"[ItemManager] Failed to parse nested stats: {e.Message}"); }
    }

    private float ParseFloat(string val)
    {
        if (float.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float result)) return result;
        return 0f;
    }

    private string GetValue(Dictionary<string, string> dict, params string[] potentialKeys)
    {
        foreach (var key in potentialKeys) if (dict.ContainsKey(key)) return dict[key];
        return null;
    }
}
