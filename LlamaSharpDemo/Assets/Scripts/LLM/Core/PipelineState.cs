using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Mutable shared state passed between prompt pipeline steps.
/// String values drive prompt rendering while the object channel carries richer runtime data.
/// </summary>
public sealed class PipelineState
{
    private readonly Dictionary<string, string> _textValues;
    private readonly Dictionary<string, object> _objectValues;

    public PipelineState()
    {
        _textValues = new Dictionary<string, string>(StringComparer.Ordinal);
        _objectValues = new Dictionary<string, object>(StringComparer.Ordinal);
    }

    public PipelineState(PipelineState source)
        : this()
    {
        if (source == null)
        {
            return;
        }

        foreach (var kvp in source._textValues)
        {
            _textValues[kvp.Key] = kvp.Value;
        }

        foreach (var kvp in source._objectValues)
        {
            _objectValues[kvp.Key] = kvp.Value;
        }
    }

    public IEnumerable<string> AllKeys
    {
        get
        {
            var yielded = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _textValues.Keys)
            {
                if (yielded.Add(key))
                {
                    yield return key;
                }
            }

            foreach (var key in _objectValues.Keys)
            {
                if (yielded.Add(key))
                {
                    yield return key;
                }
            }
        }
    }

    public IEnumerable<KeyValuePair<string, string>> TextEntries => _textValues;

    public PipelineState Clone()
    {
        return new PipelineState(this);
    }

    public void Clear()
    {
        _textValues.Clear();
        _objectValues.Clear();
    }

    public void SetString(string key, string value)
    {
        string normalizedKey = NormalizeKey(key);
        _objectValues.Remove(normalizedKey);
        _textValues[normalizedKey] = value ?? string.Empty;
    }

    public bool TryGetString(string key, out string value)
    {
        return _textValues.TryGetValue(NormalizeKey(key), out value);
    }

    public string GetString(string key, string fallback = "")
    {
        return TryGetString(key, out string value) ? value : fallback;
    }

    public bool ContainsString(string key)
    {
        return _textValues.ContainsKey(NormalizeKey(key));
    }

    public void SetObject(string key, object value)
    {
        string normalizedKey = NormalizeKey(key);
        if (value == null)
        {
            Remove(normalizedKey);
            return;
        }

        _textValues.Remove(normalizedKey);
        _objectValues[normalizedKey] = value;
    }

    public bool TryGetObject(string key, out object value)
    {
        return _objectValues.TryGetValue(NormalizeKey(key), out value);
    }

    public bool TryGetObject<T>(string key, out T value) where T : class
    {
        if (TryGetObject(key, out object raw) && raw is T cast)
        {
            value = cast;
            return true;
        }

        value = null;
        return false;
    }

    public bool TryGetImageSource(string key, out UnityEngine.Object imageSource)
    {
        imageSource = null;
        if (!TryGetObject(key, out object raw))
        {
            return false;
        }

        switch (raw)
        {
            case Texture2D texture:
                imageSource = texture;
                return true;
            case Sprite sprite:
                imageSource = sprite;
                return true;
            case UnityEngine.Object unityObject when unityObject is Texture2D || unityObject is Sprite:
                imageSource = unityObject;
                return true;
            default:
                return false;
        }
    }

    public void SetImage(string key, UnityEngine.Object image)
    {
        if (image != null && image is not Texture2D && image is not Sprite)
        {
            throw new ArgumentException("PipelineState only supports Texture2D or Sprite image values.", nameof(image));
        }

        SetObject(key, image);
    }

    public bool Remove(string key)
    {
        string normalizedKey = NormalizeKey(key);
        bool removed = _textValues.Remove(normalizedKey);
        removed |= _objectValues.Remove(normalizedKey);
        return removed;
    }

    public bool HasAnyValue(string key)
    {
        string normalizedKey = NormalizeKey(key);
        return _textValues.ContainsKey(normalizedKey) || _objectValues.ContainsKey(normalizedKey);
    }

    public string GetDisplayValue(string key)
    {
        string normalizedKey = NormalizeKey(key);
        if (_textValues.TryGetValue(normalizedKey, out string text))
        {
            return text;
        }

        if (_objectValues.TryGetValue(normalizedKey, out object raw))
        {
            return DescribeValue(raw);
        }

        return string.Empty;
    }

    public string GetTemplateValue(string key)
    {
        return GetDisplayValue(key);
    }

    public static string DescribeValue(object value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        switch (value)
        {
            case string text:
                return text;
            case Texture2D texture:
                return $"[Texture2D {texture.width}x{texture.height}: {texture.name}]";
            case Sprite sprite:
                Rect rect = sprite.rect;
                return $"[Sprite {rect.width.ToString(CultureInfo.InvariantCulture)}x{rect.height.ToString(CultureInfo.InvariantCulture)}: {sprite.name}]";
            case UnityEngine.Object unityObject:
                return $"[{unityObject.GetType().Name}: {unityObject.name}]";
            default:
                return value.ToString() ?? string.Empty;
        }
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("State key cannot be null or whitespace.", nameof(key));
        }

        return key.Trim();
    }
}
