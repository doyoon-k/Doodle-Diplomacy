using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "AI Fighter/Item")]
public class ItemData : ScriptableObject
{
    [Header("Item Info")]
    public string itemName;
    [TextArea(3, 10)]
    public string description;
    public Sprite icon;

    [Header("Cache")]
    public bool isCached = false;
    public string cachedStatModelJson;
    public string cachedSkillModelJson;
}