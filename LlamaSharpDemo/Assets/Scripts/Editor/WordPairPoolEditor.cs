#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DoodleDiplomacy.Data;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Editor
{
    [CustomEditor(typeof(WordPairPool))]
    public class WordPairPoolEditor : UnityEditor.Editor
    {
        private SerializedProperty _pairsProp;
        private readonly List<int> _matchedIndices = new();
        private string _searchText = string.Empty;
        private Vector2 _matchScroll;
        private bool _showAllPairsList = true;

        private void OnEnable()
        {
            _pairsProp = serializedObject.FindProperty("pairs");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                MonoScript script = MonoScript.FromScriptableObject((WordPairPool)target);
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }

            DrawSearchSection();

            EditorGUILayout.Space(6f);
            _showAllPairsList = EditorGUILayout.Foldout(_showAllPairsList, "Pairs", true);
            if (_showAllPairsList)
            {
                EditorGUILayout.PropertyField(_pairsProp, includeChildren: true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSearchSection()
        {
            EditorGUILayout.LabelField("Find Pair", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _searchText = EditorGUILayout.TextField(_searchText);
                if (GUILayout.Button("Clear", GUILayout.Width(60f)))
                {
                    _searchText = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            string query = _searchText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                EditorGUILayout.HelpBox("Type a keyword to filter wordA / wordB / labelA / labelB.", MessageType.None);
                return;
            }

            CollectMatches(query);

            if (_matchedIndices.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No matches for \"{query}\". Checked wordA, wordB, labelA, labelB.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Found {_matchedIndices.Count} match(es) in {_pairsProp.arraySize} pair(s).",
                MessageType.Info);

            float maxHeight = Mathf.Min(420f, 90f + _matchedIndices.Count * 28f);
            _matchScroll = EditorGUILayout.BeginScrollView(_matchScroll, GUILayout.MaxHeight(maxHeight));
            for (int i = 0; i < _matchedIndices.Count; i++)
            {
                int pairIndex = _matchedIndices[i];
                DrawMatchedPair(pairIndex);
            }

            EditorGUILayout.EndScrollView();
        }

        private void CollectMatches(string query)
        {
            _matchedIndices.Clear();
            for (int i = 0; i < _pairsProp.arraySize; i++)
            {
                SerializedProperty pairProp = _pairsProp.GetArrayElementAtIndex(i);
                string wordA = pairProp.FindPropertyRelative("wordA").stringValue ?? string.Empty;
                string wordB = pairProp.FindPropertyRelative("wordB").stringValue ?? string.Empty;
                string labelA = pairProp.FindPropertyRelative("labelA").stringValue ?? string.Empty;
                string labelB = pairProp.FindPropertyRelative("labelB").stringValue ?? string.Empty;

                if (ContainsIgnoreCase(wordA, query) ||
                    ContainsIgnoreCase(wordB, query) ||
                    ContainsIgnoreCase(labelA, query) ||
                    ContainsIgnoreCase(labelB, query))
                {
                    _matchedIndices.Add(i);
                }
            }
        }

        private void DrawMatchedPair(int pairIndex)
        {
            SerializedProperty pairProp = _pairsProp.GetArrayElementAtIndex(pairIndex);
            SerializedProperty wordAProp = pairProp.FindPropertyRelative("wordA");
            SerializedProperty wordBProp = pairProp.FindPropertyRelative("wordB");
            SerializedProperty labelAProp = pairProp.FindPropertyRelative("labelA");
            SerializedProperty labelBProp = pairProp.FindPropertyRelative("labelB");

            string header = $"#{pairIndex}  {wordAProp.stringValue}  |  {wordBProp.stringValue}";
            pairProp.isExpanded = EditorGUILayout.Foldout(pairProp.isExpanded, header, true);
            if (!pairProp.isExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(wordAProp);
                EditorGUILayout.PropertyField(wordBProp);
                EditorGUILayout.PropertyField(labelAProp);
                EditorGUILayout.PropertyField(labelBProp);
            }
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(query))
            {
                return false;
            }

            return value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
