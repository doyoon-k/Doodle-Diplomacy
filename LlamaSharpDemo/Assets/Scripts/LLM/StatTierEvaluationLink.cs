using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class StatTierEvaluationLink : IStateChainLink, ICustomLinkStateProvider
{
    private readonly ScriptableObject _configAsset;
    private readonly Dictionary<string, StatRange> _statRanges = new(StringComparer.Ordinal);
    private readonly string _characterDescription;

    private readonly struct StatRange
    {
        public StatRange(float minValue, float maxValue)
        {
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public float MinValue { get; }
        public float MaxValue { get; }
    }

    public StatTierEvaluationLink(ScriptableObject asset)
    {
        _configAsset = asset;
        _characterDescription = ReadCharacterDescription(asset);
        ReadStatRanges(asset, _statRanges);

        if (_configAsset == null)
        {
            Debug.LogError("StatTierEvaluationLink requires a ScriptableObject config asset.");
        }
        else if (_statRanges.Count == 0)
        {
            Debug.LogWarning(
                $"StatTierEvaluationLink could not read stats from '{_configAsset.name}'. " +
                "Expected a GetStats() dictionary with MinValue/MaxValue on each entry.");
        }
    }

    public System.Collections.IEnumerator Execute(Dictionary<string, string> state, Action<Dictionary<string, string>> onDone)
    {
        state ??= new Dictionary<string, string>();
        var result = new Dictionary<string, string>(state);

        if (_configAsset == null || _statRanges.Count == 0)
        {
            onDone?.Invoke(result);
            yield break;
        }

        foreach (var kvp in _statRanges)
        {
            string statName = kvp.Key;
            StatRange definition = kvp.Value;

            if (result.TryGetValue(statName, out string valueStr) &&
                float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                string tier = EvaluateTier(value, definition.MinValue, definition.MaxValue);
                result[$"{statName}Tier"] = tier;
            }
        }

        if (!string.IsNullOrEmpty(_characterDescription) && !result.ContainsKey("character_description"))
        {
            result["character_description"] = _characterDescription;
        }

        onDone?.Invoke(result);
        yield break;
    }

    private string EvaluateTier(float value, float min, float max)
    {
        if (max <= min) return "medium"; // Avoid division by zero

        float t = Mathf.Clamp01((value - min) / (max - min));

        if (t < 0.2f) return "lowest";
        if (t < 0.4f) return "low";
        if (t < 0.6f) return "medium";
        if (t < 0.8f) return "high";
        return "highest";
    }

    public IEnumerable<string> GetWrites()
    {
        if (_configAsset == null || _statRanges.Count == 0)
        {
            return Enumerable.Empty<string>();
        }

        var keys = _statRanges.Keys.Select(statName => $"{statName}Tier").ToList();
        if (!string.IsNullOrEmpty(_characterDescription))
        {
            keys.Add("character_description");
        }

        return keys;
    }

    public IEnumerable<string> GetRequiredInputKeys()
    {
        if (_configAsset == null || _statRanges.Count == 0)
        {
            return Enumerable.Empty<string>();
        }

        return _statRanges.Keys;
    }

    private static void ReadStatRanges(ScriptableObject asset, Dictionary<string, StatRange> output)
    {
        if (asset == null || output == null)
        {
            return;
        }

        var getStatsMethod = asset.GetType().GetMethod(
            "GetStats",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (getStatsMethod == null)
        {
            return;
        }

        object statsObj;
        try
        {
            statsObj = getStatsMethod.Invoke(asset, null);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"StatTierEvaluationLink GetStats() invocation failed: {ex.Message}");
            return;
        }

        if (statsObj is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key == null || entry.Value == null)
                {
                    continue;
                }

                string key = entry.Key.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (TryReadMinMax(entry.Value, out float minValue, out float maxValue))
                {
                    output[key] = new StatRange(minValue, maxValue);
                }
            }
        }
    }

    private static bool TryReadMinMax(object definition, out float minValue, out float maxValue)
    {
        minValue = 0f;
        maxValue = 0f;

        if (definition == null)
        {
            return false;
        }

        var type = definition.GetType();
        if (!TryReadFloatMember(type, definition, "MinValue", out minValue) ||
            !TryReadFloatMember(type, definition, "MaxValue", out maxValue))
        {
            return false;
        }

        return true;
    }

    private static string ReadCharacterDescription(ScriptableObject asset)
    {
        if (asset == null)
        {
            return null;
        }

        var type = asset.GetType();
        if (TryReadStringMember(type, asset, "CharacterDescription", out string value))
        {
            return value;
        }

        return null;
    }

    private static bool TryReadFloatMember(Type type, object instance, string memberName, out float value)
    {
        value = 0f;

        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanRead)
        {
            object raw = property.GetValue(instance);
            if (TryConvertToFloat(raw, out value))
            {
                return true;
            }
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            object raw = field.GetValue(instance);
            if (TryConvertToFloat(raw, out value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadStringMember(Type type, object instance, string memberName, out string value)
    {
        value = null;

        var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (property != null && property.CanRead && property.PropertyType == typeof(string))
        {
            value = property.GetValue(instance) as string;
            return true;
        }

        var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null && field.FieldType == typeof(string))
        {
            value = field.GetValue(instance) as string;
            return true;
        }

        return false;
    }

    private static bool TryConvertToFloat(object raw, out float value)
    {
        value = 0f;
        if (raw == null)
        {
            return false;
        }

        switch (raw)
        {
            case float f:
                value = f;
                return true;
            case double d:
                value = (float)d;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case string s when float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed):
                value = parsed;
                return true;
            default:
                try
                {
                    value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    return false;
                }
        }
    }
}
