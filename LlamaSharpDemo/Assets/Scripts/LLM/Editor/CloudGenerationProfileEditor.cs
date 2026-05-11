#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CloudGenerationProfile))]
public sealed class CloudGenerationProfileEditor : Editor
{
    private SerializedProperty _providerProp;
    private SerializedProperty _modelIdProp;
    private SerializedProperty _baseUrlProp;
    private SerializedProperty _apiKeyEnvironmentVariableProp;
    private SerializedProperty _requestTimeoutSecondsProp;
    private SerializedProperty _maxRetriesProp;
    private SerializedProperty _retryBackoffSecondsProp;

    private SerializedProperty _formatProp;
    private SerializedProperty _streamProp;
    private SerializedProperty _keepAliveProp;
    private SerializedProperty _systemPromptTemplateProp;
    private SerializedProperty _jsonSchemaDeliveryModeProp;
    private SerializedProperty _thinkingModeProp;
    private SerializedProperty _jsonFieldsProp;
    private SerializedProperty _modelParamsProp;

    private Vector2 _formatPreviewScroll;

    private void OnEnable()
    {
        _providerProp = serializedObject.FindProperty("provider");
        _modelIdProp = serializedObject.FindProperty("modelId");
        _baseUrlProp = serializedObject.FindProperty("baseUrl");
        _apiKeyEnvironmentVariableProp = serializedObject.FindProperty("apiKeyEnvironmentVariable");
        _requestTimeoutSecondsProp = serializedObject.FindProperty("requestTimeoutSeconds");
        _maxRetriesProp = serializedObject.FindProperty("maxRetries");
        _retryBackoffSecondsProp = serializedObject.FindProperty("retryBackoffSeconds");

        _formatProp = serializedObject.FindProperty("format");
        _streamProp = serializedObject.FindProperty("stream");
        _keepAliveProp = serializedObject.FindProperty("keepAlive");
        _systemPromptTemplateProp = serializedObject.FindProperty("systemPromptTemplate");
        _jsonSchemaDeliveryModeProp = serializedObject.FindProperty("jsonSchemaDeliveryMode");
        _thinkingModeProp = serializedObject.FindProperty("thinkingMode");
        _jsonFieldsProp = serializedObject.FindProperty("jsonFields");
        _modelParamsProp = serializedObject.FindProperty("modelParams");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Cloud Provider", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_providerProp);
        EditorGUILayout.PropertyField(_modelIdProp);
        EditorGUILayout.PropertyField(_baseUrlProp);
        EditorGUILayout.PropertyField(_apiKeyEnvironmentVariableProp);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Request Policy", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_requestTimeoutSecondsProp);
        EditorGUILayout.PropertyField(_maxRetriesProp);
        EditorGUILayout.PropertyField(_retryBackoffSecondsProp);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Shared Generation Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_streamProp);
        EditorGUILayout.PropertyField(_keepAliveProp);
        EditorGUILayout.PropertyField(_systemPromptTemplateProp);
        EditorGUILayout.PropertyField(_jsonSchemaDeliveryModeProp);
        EditorGUILayout.PropertyField(_thinkingModeProp);
        EditorGUILayout.PropertyField(_modelParamsProp, true);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("JSON Output Fields", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_jsonFieldsProp, true);

        EditorGUILayout.Space(8f);
        DrawValidationStatus();
        DrawCredentialStatus();
        DrawFormatPreview();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawValidationStatus()
    {
        if (target is not CloudGenerationProfile profile)
        {
            return;
        }

        if (profile.TryValidate(out string error))
        {
            EditorGUILayout.HelpBox("Cloud profile validation passed.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox($"Cloud profile validation failed: {error}", MessageType.Error);
    }

    private void DrawCredentialStatus()
    {
        if (target is not CloudGenerationProfile profile)
        {
            return;
        }

        string envVar = profile.ResolveApiKeyEnvironmentVariable();
        if (CloudApiKeyResolver.TryResolve(profile, out _, out string source))
        {
            EditorGUILayout.HelpBox($"API key detected ({source}).", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"API key not found. Set environment variable '{envVar}' or create a CloudCredentialOverridesAsset under an Editor folder.",
                MessageType.Warning);
        }
    }

    private void DrawFormatPreview()
    {
        string preview = _formatProp == null || string.IsNullOrWhiteSpace(_formatProp.stringValue)
            ? "(no fields defined)"
            : _formatProp.stringValue;

        EditorGUILayout.LabelField("Generated Format (read-only)", EditorStyles.boldLabel);
        var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
        _formatPreviewScroll = EditorGUILayout.BeginScrollView(_formatPreviewScroll, GUILayout.MinHeight(64f));
        EditorGUILayout.SelectableLabel(preview, style, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}
#endif
