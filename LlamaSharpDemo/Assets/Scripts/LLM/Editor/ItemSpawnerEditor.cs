#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for ItemSpawner with easy-to-use buttons
/// </summary>
[CustomEditor(typeof(ItemSpawner))]
public class ItemSpawnerEditor : Editor
{
    private int selectedPresetIndex = 0;
    private readonly (string name, string desc)[] presets = new (string, string)[]
    {
        ("Phoenix Wing Coat", "Embrace the essence of the undying bird. Soar through the skies with grace and let the flames of rebirth mend your wounds instantly."),
        ("Titan's Seismic Gauntlet", "Channel the earth's fury. Strike with heavy force to shatter the ground beneath, while a protective aura hardens your skin against retaliation."),
        ("Voidwalker's Prism", "Step into the void to vanish and reappear instantly forward. Upon exit, time distorts around you, making enemies sluggish and heavy."),
        ("Berzerker's Bloodlust Drug", "A forbidden stimulant that ignores pain. Charge recklessly into the fray with blinding speed, shrugging off injuries as if you were immortal for a brief moment."),
        ("Stormcaller's Rod", "Summon the tempest. Launch a bolt of raw lightning that pierces through lines of foes, leaving them shocked and stunned in its wake."),
        ("Ninja's Smoke Bomb", "A tactical escape tool. Throw a bomb that explodes to cover your retreat, while simultaneously propelling you upwards to a vantage point."),
        ("Cryo-Stasis Field Generator", "Deploys a field of absolute zero. Enemies caught within are lifted helplessly into the frozen air, their movements grinding to a halt."),
        ("Vanguard's Charge Shield", "Raise a barrier of light to deflect harm and rush forward to slam into enemy lines, knocking them senseless."),
        ("Dragon's Breath Elixir", "Drink the liquid fire. Spew forth a devastating fireball that detonates on impact, while your inner heat accelerates your recovery."),
        ("Shadow Assassin's Cloak", "Become one with the shadows. Teleport behind your target and unleash a swift, lethal strike before they even know you're there.")
    };

    public override void OnInspectorGUI()
    {
        ItemSpawner spawner = (ItemSpawner)target;

        // Draw default inspector
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

        // Large "Spawn Items" button
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("▶ Spawn Items Now", GUILayout.Height(40)))
        {
            spawner.SpawnItems();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(5);

        // "Clear Spawned Items" button
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f); // Light red
        if (GUILayout.Button("✖ Clear All Spawned Items", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Clear Spawned Items",
                "Remove all currently spawned item instances?",
                "Yes", "Cancel"))
            {
                spawner.ClearAllSpawnedItems();
            }
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(5);

        // "Load Items from Folder" button
        GUI.backgroundColor = Color.cyan;
        if (GUILayout.Button("📁 Load All Items from Folder", GUILayout.Height(30)))
        {
            spawner.LoadAllItemsFromFolder();
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(20);

        // ==========================================
        // PRESET GENERATOR SECTION
        // ==========================================
        EditorGUILayout.LabelField("Preset Generator", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical("box");

        string[] presetNames = new string[presets.Length];
        for (int i = 0; i < presets.Length; i++) presetNames[i] = presets[i].name;

        selectedPresetIndex = EditorGUILayout.Popup("Choose Preset", selectedPresetIndex, presetNames);

        if (selectedPresetIndex >= 0 && selectedPresetIndex < presets.Length)
        {
            var (pName, pDesc) = presets[selectedPresetIndex];
            EditorGUILayout.LabelField("Description:", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(pDesc, MessageType.None);

            EditorGUILayout.Space(5);

            GUI.backgroundColor = new Color(0.7f, 0.7f, 1f); // Light blue
            if (GUILayout.Button("✨ Create Asset & Add to Spawner", GUILayout.Height(30)))
            {
                CreateItemAsset(spawner, pName, pDesc);
            }
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // Info section
        EditorGUILayout.HelpBox(
            $"Items to spawn: {spawner.itemsToSpawn.Count}\n" +
            $"Currently spawned: {spawner.transform.childCount}\n\n" +
            "Adjust spawn area using Scene Gizmos (yellow box)",
            MessageType.Info);

        // Scene view tip
        if (spawner.itemsToSpawn.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No items loaded! Use 'Load All Items from Folder' or create a preset above.",
                MessageType.Warning);
        }
    }

    private void CreateItemAsset(ItemSpawner spawner, string name, string desc)
    {
        string folderPath = "Assets/ScriptableObjects/Items/Generated";
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        string fileName = $"Item_{name.Replace(" ", "").Replace("'", "")}.asset";
        string fullPath = $"{folderPath}/{fileName}";

        // Check if asset already exists
        ItemData existingItem = AssetDatabase.LoadAssetAtPath<ItemData>(fullPath);
        if (existingItem != null)
        {
            if (!EditorUtility.DisplayDialog("Item Exists", $"Item {fileName} already exists. Overwrite?", "Yes", "No"))
            {
                return;
            }
        }

        ItemData newItem = ScriptableObject.CreateInstance<ItemData>();
        newItem.itemName = name;
        newItem.description = desc;

        if (existingItem != null)
        {
            // Update existing
            existingItem.itemName = name;
            existingItem.description = desc;
            EditorUtility.SetDirty(existingItem);
            newItem = existingItem;
        }
        else
        {
            AssetDatabase.CreateAsset(newItem, fullPath);
        }

        AssetDatabase.SaveAssets();

        // Add to spawner list if not already there
        if (!spawner.itemsToSpawn.Contains(newItem))
        {
            spawner.itemsToSpawn.Add(newItem);
        }

        EditorUtility.SetDirty(spawner);
        Debug.Log($"[ItemSpawnerEditor] Created/Updated asset: {fullPath} and added to spawner.");

        // Highlight the new asset
        EditorGUIUtility.PingObject(newItem);
    }

    // Draw handles in Scene view for easy position adjustment
    private void OnSceneGUI()
    {
        ItemSpawner spawner = (ItemSpawner)target;

        // Draw spawn area handles
        Vector3 center = new Vector3(spawner.spawnCenter.x, spawner.spawnHeight, spawner.spawnCenter.y);

        // Position handle for spawn center
        EditorGUI.BeginChangeCheck();
        Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(spawner, "Move Spawn Center");
            spawner.spawnCenter = new Vector2(newCenter.x, newCenter.z);
            spawner.spawnHeight = newCenter.y;
            EditorUtility.SetDirty(spawner);
        }

        // Draw labels
        Handles.Label(center + Vector3.up, "Spawn Center", EditorStyles.whiteLargeLabel);

        // Draw spawn area bounds
        Handles.color = Color.yellow;
        Vector3[] corners = new Vector3[5];
        float halfWidth = spawner.spawnAreaSize.x / 2f;
        float halfHeight = spawner.spawnAreaSize.y / 2f;

        corners[0] = center + new Vector3(-halfWidth, 0, -halfHeight);
        corners[1] = center + new Vector3(halfWidth, 0, -halfHeight);
        corners[2] = center + new Vector3(halfWidth, 0, halfHeight);
        corners[3] = center + new Vector3(-halfWidth, 0, halfHeight);
        corners[4] = corners[0]; // Close the loop

        Handles.DrawPolyLine(corners);
    }
}
#endif
