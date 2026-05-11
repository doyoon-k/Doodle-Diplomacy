using System;
using UnityEngine;

public static class LlmSettingsChangeNotifier
{
    public static event Action<BaseLlmGenerationProfile> SettingsChanged;

    public static void RaiseChanged(BaseLlmGenerationProfile settings)
    {
        if (settings == null)
        {
            return;
        }

        SettingsChanged?.Invoke(settings);
    }
}
