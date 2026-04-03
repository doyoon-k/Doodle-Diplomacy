#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StableDiffusionCppModelProfile))]
public class StableDiffusionCppModelProfileEditor : Editor
{
    private static readonly string[] SamplerOptions =
    {
        "euler_a",
        "euler",
        "heun",
        "dpm2",
        "dpm++2s_a",
        "dpm++2m",
        "dpm++2mv2",
        "ipndm",
        "ipndm_v",
        "lcm",
        "ddim",
        "tcd"
    };

    private static readonly string[] SchedulerOptions =
    {
        "discrete",
        "karras",
        "exponential",
        "ays",
        "gits",
        "smoothstep",
        "sgm_uniform",
        "simple",
        "kl_optimal",
        "lcm",
        "bong_tangent"
    };

    private SerializedProperty _profileNameProp;
    private SerializedProperty _modelPathProp;
    private SerializedProperty _vaePathProp;
    private SerializedProperty _controlNetPathProp;
    private SerializedProperty _defaultWidthProp;
    private SerializedProperty _defaultHeightProp;
    private SerializedProperty _defaultStepsProp;
    private SerializedProperty _defaultCfgScaleProp;
    private SerializedProperty _defaultSeedProp;
    private SerializedProperty _defaultSamplerProp;
    private SerializedProperty _defaultSchedulerProp;
    private SerializedProperty _defaultNegativePromptProp;
    private SerializedProperty _defaultControlStrengthProp;

    private void OnEnable()
    {
        _profileNameProp = serializedObject.FindProperty("profileName");
        _modelPathProp = serializedObject.FindProperty("modelPath");
        _vaePathProp = serializedObject.FindProperty("vaePath");
        _controlNetPathProp = serializedObject.FindProperty("controlNetPath");
        _defaultWidthProp = serializedObject.FindProperty("defaultWidth");
        _defaultHeightProp = serializedObject.FindProperty("defaultHeight");
        _defaultStepsProp = serializedObject.FindProperty("defaultSteps");
        _defaultCfgScaleProp = serializedObject.FindProperty("defaultCfgScale");
        _defaultSeedProp = serializedObject.FindProperty("defaultSeed");
        _defaultSamplerProp = serializedObject.FindProperty("defaultSampler");
        _defaultSchedulerProp = serializedObject.FindProperty("defaultScheduler");
        _defaultNegativePromptProp = serializedObject.FindProperty("defaultNegativePrompt");
        _defaultControlStrengthProp = serializedObject.FindProperty("defaultControlStrength");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            MonoScript script = MonoScript.FromScriptableObject((StableDiffusionCppModelProfile)target);
            EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
        }

        EditorGUILayout.PropertyField(_profileNameProp);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_modelPathProp);
        EditorGUILayout.PropertyField(_vaePathProp);
        EditorGUILayout.PropertyField(_controlNetPathProp);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Generation Defaults", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_defaultWidthProp);
        EditorGUILayout.PropertyField(_defaultHeightProp);
        EditorGUILayout.PropertyField(_defaultStepsProp);
        EditorGUILayout.PropertyField(_defaultCfgScaleProp);
        EditorGUILayout.PropertyField(_defaultSeedProp);
        DrawDropdownStringProperty(_defaultSamplerProp, "Default Sampler", SamplerOptions);
        DrawDropdownStringProperty(_defaultSchedulerProp, "Default Scheduler", SchedulerOptions);
        EditorGUILayout.PropertyField(_defaultNegativePromptProp);
        EditorGUILayout.PropertyField(_defaultControlStrengthProp);

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawDropdownStringProperty(
        SerializedProperty property,
        string label,
        IReadOnlyList<string> options)
    {
        string currentValue = property.stringValue ?? string.Empty;
        int selectedIndex = FindOptionIndex(options, currentValue);
        string[] displayOptions = BuildDisplayOptions(options, currentValue, selectedIndex);
        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        int newIndex = EditorGUILayout.Popup(label, selectedIndex, displayOptions);
        if (newIndex >= 0 && newIndex < displayOptions.Length)
        {
            property.stringValue = displayOptions[newIndex];
        }
    }

    private static int FindOptionIndex(IReadOnlyList<string> options, string currentValue)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], currentValue, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string[] BuildDisplayOptions(
        IReadOnlyList<string> options,
        string currentValue,
        int selectedIndex)
    {
        if (selectedIndex >= 0 || string.IsNullOrWhiteSpace(currentValue))
        {
            var values = new string[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                values[i] = options[i];
            }

            return values;
        }

        var merged = new string[options.Count + 1];
        merged[0] = currentValue;
        for (int i = 0; i < options.Count; i++)
        {
            merged[i + 1] = options[i];
        }

        return merged;
    }
}
#endif
