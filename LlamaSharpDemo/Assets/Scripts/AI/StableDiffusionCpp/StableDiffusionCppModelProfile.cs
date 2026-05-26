using UnityEngine;

[CreateAssetMenu(fileName = "StableDiffusionModelProfile", menuName = "AI/Stable Diffusion CPP Model Profile")]
public class StableDiffusionCppModelProfile : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("User-facing profile label.")]
    public string profileName = "SD 1.5";

    [Header("Model")]
    [Tooltip("Model path relative to StreamingAssets, or absolute path.")]
    public string modelPath = "SDModels/v1-5-pruned-emaonly-q4_K.gguf";

    [Tooltip("Optional VAE path relative to StreamingAssets, or absolute path.")]
    public string vaePath = string.Empty;

    [Tooltip("Optional ControlNet model path relative to StreamingAssets, or absolute path.")]
    public string controlNetPath = "SDModels/control_v11p_sd15_scribble-q4_K.gguf";

    [Header("Generation Defaults")]
    [Min(64)]
    [Tooltip("Default generated image width in pixels.")]
    public int defaultWidth = 512;

    [Min(64)]
    [Tooltip("Default generated image height in pixels.")]
    public int defaultHeight = 512;

    [Min(1)]
    [Tooltip("Default diffusion step count. Higher values are slower but can improve quality.")]
    public int defaultSteps = 20;

    [Min(0.1f)]
    [Tooltip("Default classifier-free guidance scale. Higher values follow the prompt more strongly.")]
    public float defaultCfgScale = 7.0f;

    [Tooltip("Default image generation seed. Use a fixed value for repeatable outputs.")]
    public int defaultSeed = 42;
    [Tooltip("Default stable-diffusion.cpp sampler name.")]
    public string defaultSampler = "euler_a";
    [Tooltip("Default stable-diffusion.cpp scheduler name.")]
    public string defaultScheduler = "discrete";

    [TextArea(2, 6)]
    [Tooltip("Default negative prompt appended to generation requests.")]
    public string defaultNegativePrompt = string.Empty;

    [Range(0f, 2f)]
    [Tooltip("Default ControlNet strength for requests that include a control image.")]
    public float defaultControlStrength = 0.9f;

    public string DisplayName => string.IsNullOrWhiteSpace(profileName) ? name : profileName.Trim();

    public void ApplyDefaultsTo(StableDiffusionCppGenerationRequest request)
    {
        if (request == null)
        {
            return;
        }

        request.width = defaultWidth;
        request.height = defaultHeight;
        request.steps = defaultSteps;
        request.cfgScale = defaultCfgScale;
        request.seed = defaultSeed;
        request.sampler = string.IsNullOrWhiteSpace(defaultSampler) ? "euler_a" : defaultSampler.Trim();
        request.scheduler = string.IsNullOrWhiteSpace(defaultScheduler) ? "discrete" : defaultScheduler.Trim();
        request.controlStrength = Mathf.Clamp(defaultControlStrength, 0f, 2f);
        request.negativePrompt = defaultNegativePrompt ?? string.Empty;
    }

    private void OnValidate()
    {
        profileName = string.IsNullOrWhiteSpace(profileName) ? "Stable Diffusion Profile" : profileName.Trim();
        defaultWidth = Mathf.Max(64, defaultWidth);
        defaultHeight = Mathf.Max(64, defaultHeight);
        defaultSteps = Mathf.Max(1, defaultSteps);
        defaultCfgScale = Mathf.Max(0.1f, defaultCfgScale);
        defaultSampler = string.IsNullOrWhiteSpace(defaultSampler) ? "euler_a" : defaultSampler.Trim();
        defaultScheduler = string.IsNullOrWhiteSpace(defaultScheduler) ? "discrete" : defaultScheduler.Trim();
        defaultControlStrength = Mathf.Clamp(defaultControlStrength, 0f, 2f);
        defaultNegativePrompt ??= string.Empty;
    }
}
