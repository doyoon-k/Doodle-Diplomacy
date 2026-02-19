#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to automatically create 15 ItemData ScriptableObjects
/// Usage: Tools > Item Generator > Create All Items
/// </summary>
public class ItemDataGenerator : EditorWindow
{
    private static readonly (string name, string description)[] ItemDefinitions = new[]
    {
        ("Spicy Chili", "A small pepper with an intense heat that spreads faster than expected. It doesn't last long, but while it burns, it's hard to slow down."),
        ("Sweet Candy", "A brightly wrapped candy that melts quickly on the tongue. The sweetness is comforting at first, then strangely overwhelming."),
        ("Ice Cube", "A simple block of ice, cold enough to numb your fingers. It keeps its shape only if you handle it carefully."),
        ("Stale Bread", "Dry, tasteless bread that's harder to chew than it should be. It fills you up, even if you don't enjoy it."),
        ("Hot Coffee", "A cup of coffee that's almost too hot to drink. The warmth wakes you up sharply, but one wrong move and it spills everywhere."),
        ("Energy Drink", "A fizzy can that promises more alertness than you asked for. The jittery feeling kicks in fast and doesn't fade quietly."),
        ("Cold Rain", "Thin rain soaking through clothes within minutes. Movements feel heavier, and anything exposed stays cold for a while."),
        ("Dry Wind", "A steady wind that scrapes across exposed skin. It doesn't knock you down, but it refuses to be ignored."),
        ("Sticky Syrup", "Thick liquid that clings to everything it touches. Easy to spill, difficult to clean up."),
        ("Sharp Sugar Glass", "Candy hardened into clear, brittle shards. It looks harmless until it cracks the moment you bite down."),
        ("Bitter Medicine", "A small dose with an unpleasant aftertaste. It's not enjoyable, but you can feel it working somewhere inside."),
        ("Warm Soup", "A simple bowl that warms your hands before you even taste it. It settles slowly, spreading comfort through your body."),
        ("Static Shock", "A tiny jolt that snaps through your fingers when you touch metal. It's brief, startling, and impossible to predict."),
        ("Heavy Sleep", "The kind of tiredness that pulls your limbs downward. Moving still works—it just takes more effort than before."),
        ("Fresh Sugar Rush", "A sudden burst of sweetness that spikes your senses. Thoughts race ahead faster than your body keeps up.")
    };

    [MenuItem("Tools/Item Generator/Create All Items")]
    public static void CreateAllItems()
    {
        string basePath = "Assets/ScriptableObjects/Items";

        // Ensure directory exists
        if (!AssetDatabase.IsValidFolder(basePath))
        {
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "Items");
        }

        int created = 0;
        int skipped = 0;

        foreach (var (name, description) in ItemDefinitions)
        {
            string fileName = name.Replace(" ", "");
            string assetPath = $"{basePath}/{fileName}.asset";

            // Check if already exists
            if (AssetDatabase.LoadAssetAtPath<ItemData>(assetPath) != null)
            {
                Debug.Log($"[ItemGenerator] Skipped (already exists): {name}");
                skipped++;
                continue;
            }

            // Create new ItemData
            ItemData newItem = ScriptableObject.CreateInstance<ItemData>();
            newItem.itemName = name;
            newItem.description = description;
            newItem.isCached = false;

            // Save asset
            AssetDatabase.CreateAsset(newItem, assetPath);
            created++;
            Debug.Log($"[ItemGenerator] Created: {name} at {assetPath}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=green>[ItemGenerator] Complete! Created: {created}, Skipped: {skipped}</color>");
        EditorUtility.DisplayDialog("Item Generation Complete",
            $"Created {created} new items\nSkipped {skipped} existing items\n\nLocation: {basePath}", "OK");
    }

    [MenuItem("Tools/Item Generator/Clear All Items")]
    public static void ClearAllItems()
    {
        if (!EditorUtility.DisplayDialog("Clear All Items",
            "This will DELETE all generated ItemData assets. This cannot be undone!\n\nAre you sure?",
            "Yes, Delete", "Cancel"))
        {
            return;
        }

        string basePath = "Assets/ScriptableObjects/Items";
        int deleted = 0;

        foreach (var (name, _) in ItemDefinitions)
        {
            string fileName = name.Replace(" ", "");
            string assetPath = $"{basePath}/{fileName}.asset";

            if (AssetDatabase.LoadAssetAtPath<ItemData>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
                deleted++;
                Debug.Log($"[ItemGenerator] Deleted: {name}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"<color=yellow>[ItemGenerator] Deleted {deleted} items</color>");
        EditorUtility.DisplayDialog("Clear Complete", $"Deleted {deleted} items", "OK");
    }
}
#endif
