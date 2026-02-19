using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ItemCreatorUITool : EditorWindow
{
    [MenuItem("Tools/Create Item Creator UI")]
    public static void CreateItemCreatorUI()
    {
        // 1. Ensure Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("Canvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            Debug.Log("Created new Canvas");
        }

        // 2. Ensure EventSystem
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
            Debug.Log("Created EventSystem");
        }

        // 3. Create Main Controller Object
        GameObject uiControllerObj = new GameObject("ItemCreatorUI");
        uiControllerObj.transform.SetParent(canvas.transform, false);

        // 4. Create Panel (Window)
        GameObject panelObj = new GameObject("WindowPanel");
        panelObj.transform.SetParent(uiControllerObj.transform, false);
        Image panelImage = panelObj.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f); // Opaque dark background

        RectTransform panelRect = panelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(400, 300);

        // Title
        GameObject titleObj = CreateText(panelObj.transform, "Title", "Create Custom Item", 24, Color.white);
        titleObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 120);

        // Name Input
        GameObject nameInputObj = CreateInputField(panelObj.transform, "NameInput", "Enter Item Name...");
        nameInputObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, 50);

        // Description Input
        GameObject descInputObj = CreateInputField(panelObj.transform, "DescInput", "Enter Description...");
        descInputObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -30);
        descInputObj.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 80); // Taller for description

        // Generate Button
        GameObject generateBtnObj = CreateButton(panelObj.transform, "GenerateBtn", "Generate Item");
        generateBtnObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -110);
        generateBtnObj.GetComponent<Image>().color = new Color(0.2f, 0.6f, 0.2f); // Greenish

        // Preset Button
        GameObject presetBtnObj = CreateButton(panelObj.transform, "PresetBtn", "Cycle Preset");
        presetBtnObj.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -160);
        presetBtnObj.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.8f); // Blueish

        // 5. Create Toggle Button (Outside Panel)
        GameObject toggleBtnObj = CreateButton(uiControllerObj.transform, "OpenButton", "New Item");
        RectTransform toggleRect = toggleBtnObj.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(1, 0); // Bottom Right
        toggleRect.anchorMax = new Vector2(1, 0);
        toggleRect.pivot = new Vector2(1, 0);
        toggleRect.anchoredPosition = new Vector2(-20, 20); // Padding

        // 6. Add Component and Link
        ItemCreatorUI uiScript = uiControllerObj.AddComponent<ItemCreatorUI>();

        uiScript.windowPanel = panelObj;
        uiScript.nameInput = nameInputObj.GetComponent<InputField>();
        uiScript.descriptionInput = descInputObj.GetComponent<InputField>();
        uiScript.generateButton = generateBtnObj.GetComponent<Button>();
        uiScript.toggleButton = toggleBtnObj.GetComponent<Button>();

        // Auto-link scene references
        uiScript.itemSpawner = FindObjectOfType<ItemSpawner>();
        uiScript.playerController = FindObjectOfType<PlayerController>();

        // Initial State
        panelObj.SetActive(false);

        Selection.activeGameObject = uiControllerObj;
        Debug.Log("Item Creator UI Created Successfully!");
        Undo.RegisterCreatedObjectUndo(uiControllerObj, "Create Item Creator UI");
    }

    private static GameObject CreateText(Transform parent, string name, string content, int fontSize, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Text text = obj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return obj;
    }

    private static GameObject CreateInputField(Transform parent, string name, string placeholderText)
    {
        GameObject inputObj = new GameObject(name);
        inputObj.transform.SetParent(parent, false);
        Image bg = inputObj.AddComponent<Image>();
        bg.color = Color.white;

        RectTransform rt = inputObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(300, 40);

        // Text Area
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(inputObj.transform, false);
        Text text = textObj.AddComponent<Text>();
        text.color = Color.black;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(10, 0);
        textRT.offsetMax = new Vector2(-10, 0);

        // Placeholder
        GameObject placeObj = new GameObject("Placeholder");
        placeObj.transform.SetParent(inputObj.transform, false);
        Text placeText = placeObj.AddComponent<Text>();
        placeText.text = placeholderText;
        placeText.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        placeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        placeText.fontSize = 18;
        placeText.fontStyle = FontStyle.Italic;
        placeText.alignment = TextAnchor.MiddleLeft;
        RectTransform placeRT = placeObj.GetComponent<RectTransform>();
        placeRT.anchorMin = Vector2.zero;
        placeRT.anchorMax = Vector2.one;
        placeRT.offsetMin = new Vector2(10, 0);
        placeRT.offsetMax = new Vector2(-10, 0);

        InputField inputField = inputObj.AddComponent<InputField>();
        inputField.textComponent = text;
        inputField.placeholder = placeText;

        return inputObj;
    }

    private static GameObject CreateButton(Transform parent, string name, string label)
    {
        GameObject btnObj = new GameObject(name);
        btnObj.transform.SetParent(parent, false);
        Image img = btnObj.AddComponent<Image>();
        img.color = Color.white;

        Button btn = btnObj.AddComponent<Button>();

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(160, 40);

        GameObject textObj = CreateText(btnObj.transform, "Text", label, 18, Color.black);
        RectTransform textRT = textObj.GetComponent<RectTransform>();
        textRT.anchorMin = Vector2.zero;
        textRT.anchorMax = Vector2.one;
        textRT.offsetMin = Vector2.zero;
        textRT.offsetMax = Vector2.zero;

        return btnObj;
    }
}
