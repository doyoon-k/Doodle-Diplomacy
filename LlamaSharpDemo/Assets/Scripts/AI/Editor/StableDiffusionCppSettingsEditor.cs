#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StableDiffusionCppSettings))]
public class StableDiffusionCppSettingsEditor : Editor
{
    private SerializedProperty _modelProfilesProp;
    private SerializedProperty _activeModelProfileProp;

    private void OnEnable()
    {
        _modelProfilesProp = serializedObject.FindProperty("modelProfiles");
        _activeModelProfileProp = serializedObject.FindProperty("activeModelProfile");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            MonoScript script = MonoScript.FromScriptableObject((StableDiffusionCppSettings)target);
            EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
        }

        DrawProfilesSection();

        EditorGUILayout.Space(6f);
        DrawPropertiesExcluding(serializedObject, "m_Script", "modelProfiles", "activeModelProfile");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawProfilesSection()
    {
        EditorGUILayout.LabelField("Model Profiles", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_modelProfilesProp, includeChildren: true);

        BuildProfilePopupOptions(out List<StableDiffusionCppModelProfile> profiles, out string[] labels);
        if (profiles.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "No model profiles assigned. Add profile assets or create defaults.",
                MessageType.Warning);
        }
        else
        {
            int selectedIndex = FindActiveProfileIndex(profiles);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
                _activeModelProfileProp.objectReferenceValue = profiles[0];
            }

            int newIndex = EditorGUILayout.Popup("Active Profile", selectedIndex, labels);
            if (newIndex >= 0 && newIndex < profiles.Count)
            {
                _activeModelProfileProp.objectReferenceValue = profiles[newIndex];
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Recommended Defaults"))
            {
                CreateDefaultProfilesAndAssign();
            }

            if (GUILayout.Button("Auto-Assign Existing Profiles"))
            {
                AssignExistingProfiles();
            }
        }
    }

    private int FindActiveProfileIndex(IReadOnlyList<StableDiffusionCppModelProfile> profiles)
    {
        var active = _activeModelProfileProp.objectReferenceValue as StableDiffusionCppModelProfile;
        if (active == null)
        {
            return -1;
        }

        for (int i = 0; i < profiles.Count; i++)
        {
            if (profiles[i] == active)
            {
                return i;
            }
        }

        return -1;
    }

    private void BuildProfilePopupOptions(
        out List<StableDiffusionCppModelProfile> profiles,
        out string[] labels)
    {
        profiles = new List<StableDiffusionCppModelProfile>();
        var labelList = new List<string>();

        for (int i = 0; i < _modelProfilesProp.arraySize; i++)
        {
            SerializedProperty element = _modelProfilesProp.GetArrayElementAtIndex(i);
            var profile = element.objectReferenceValue as StableDiffusionCppModelProfile;
            if (profile == null)
            {
                continue;
            }

            profiles.Add(profile);
            labelList.Add(profile.DisplayName);
        }

        labels = labelList.ToArray();
    }

    private void AssignExistingProfiles()
    {
        string[] guids = AssetDatabase.FindAssets("t:StableDiffusionCppModelProfile");
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            StableDiffusionCppModelProfile profile = AssetDatabase.LoadAssetAtPath<StableDiffusionCppModelProfile>(path);
            if (profile == null)
            {
                continue;
            }

            AddProfileIfMissing(profile);
        }

        if (_activeModelProfileProp.objectReferenceValue == null && _modelProfilesProp.arraySize > 0)
        {
            _activeModelProfileProp.objectReferenceValue = _modelProfilesProp.GetArrayElementAtIndex(0).objectReferenceValue;
        }
    }

    private void CreateDefaultProfilesAndAssign()
    {
        var settings = target as StableDiffusionCppSettings;
        if (settings == null)
        {
            return;
        }

        StableDiffusionCppSetupUtility.CreateOrRefreshDefaultProfiles(settings);
        serializedObject.Update();
    }

    private void AddProfileIfMissing(StableDiffusionCppModelProfile profile)
    {
        if (profile == null)
        {
            return;
        }

        for (int i = 0; i < _modelProfilesProp.arraySize; i++)
        {
            SerializedProperty element = _modelProfilesProp.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue == profile)
            {
                return;
            }
        }

        int newIndex = _modelProfilesProp.arraySize;
        _modelProfilesProp.arraySize = newIndex + 1;
        _modelProfilesProp.GetArrayElementAtIndex(newIndex).objectReferenceValue = profile;
    }
}
#endif
