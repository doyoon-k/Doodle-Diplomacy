using System;
using UnityEngine;

public static class LlmSettingsChangeNotifier
{
    public static event Action<LlmGenerationProfile> SettingsChanged;

    public static void RaiseChanged(LlmGenerationProfile settings)
    {
        if (settings == null)
        {
            return;
        }

        SettingsChanged?.Invoke(settings);
    }
}

