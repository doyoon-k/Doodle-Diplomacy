using System.Text;
using UnityEngine;

public class VisionPipelineSampleRunner : MonoBehaviour
{
    [Header("Pipeline")]
    [SerializeField] private PromptPipelineAsset pipelineAsset;
    [SerializeField] private string analysisGoal = "Summarize the image for a Unity content pipeline.";

    [Header("Image Inputs")]
    [SerializeField] private Texture2D textureAsset;
    [SerializeField] private Sprite spriteAsset;

    [Header("Debug")]
    [SerializeField] private bool logFullState = true;

    [ContextMenu("Run Sample/Use Texture Asset")]
    public void RunWithTextureAsset()
    {
        RunWithImage(textureAsset, false);
    }

    [ContextMenu("Run Sample/Use Sprite Asset")]
    public void RunWithSpriteAsset()
    {
        RunWithImage(spriteAsset, false);
    }

    [ContextMenu("Run Sample/Use Generated Texture")]
    public void RunWithGeneratedTexture()
    {
        Texture2D runtimeTexture = CreateRuntimeSampleTexture();
        RunWithImage(runtimeTexture, true);
    }

    private void RunWithImage(Object imageSource, bool ownsRuntimeTexture)
    {
        if (pipelineAsset == null)
        {
            Debug.LogError("[VisionPipelineSampleRunner] Pipeline asset is not assigned.");
            CleanupOwnedImage(imageSource, ownsRuntimeTexture);
            return;
        }

        if (GamePipelineRunner.Instance == null)
        {
            Debug.LogError("[VisionPipelineSampleRunner] GamePipelineRunner.Instance is missing.");
            CleanupOwnedImage(imageSource, ownsRuntimeTexture);
            return;
        }

        if (imageSource == null)
        {
            Debug.LogError("[VisionPipelineSampleRunner] Image input is null.");
            CleanupOwnedImage(imageSource, ownsRuntimeTexture);
            return;
        }

        if (imageSource is not Texture2D && imageSource is not Sprite)
        {
            Debug.LogError(
                $"[VisionPipelineSampleRunner] Unsupported image input type '{imageSource.GetType().Name}'. Use Texture2D or Sprite.");
            CleanupOwnedImage(imageSource, ownsRuntimeTexture);
            return;
        }

        var state = new PipelineState();
        state.SetString("analysis_goal", analysisGoal ?? string.Empty);
        state.SetImage("reference_image", imageSource);

        Debug.Log($"[VisionPipelineSampleRunner] Running '{pipelineAsset.name}' with {PipelineState.DescribeValue(imageSource)}");

        GamePipelineRunner.Instance.RunPipeline(pipelineAsset, state, finalState =>
        {
            try
            {
                OnPipelineCompleted(finalState);
            }
            finally
            {
                CleanupOwnedImage(imageSource, ownsRuntimeTexture);
            }
        });
    }

    private void OnPipelineCompleted(PipelineState finalState)
    {
        if (finalState == null)
        {
            Debug.LogError("[VisionPipelineSampleRunner] Pipeline returned null state.");
            return;
        }

        if (finalState.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
            !string.IsNullOrWhiteSpace(pipelineError))
        {
            Debug.LogError($"[VisionPipelineSampleRunner] Pipeline error: {pipelineError}");
        }

        LogStateKey(finalState, "image_summary");
        LogStateKey(finalState, "primary_subject");
        LogStateKey(finalState, "notable_objects");
        LogStateKey(finalState, "visual_tags");

        if (logFullState)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[VisionPipelineSampleRunner] Full PipelineState:");
            foreach (string key in finalState.AllKeys)
            {
                builder.Append("- ");
                builder.Append(key);
                builder.Append(": ");
                builder.AppendLine(finalState.GetDisplayValue(key));
            }

            Debug.Log(builder.ToString());
        }
    }

    private static void LogStateKey(PipelineState state, string key)
    {
        if (state == null)
        {
            return;
        }

        if (!state.HasAnyValue(key))
        {
            Debug.LogWarning($"[VisionPipelineSampleRunner] State key '{key}' is missing.");
            return;
        }

        Debug.Log($"[VisionPipelineSampleRunner] {key}: {state.GetDisplayValue(key)}");
    }

    private static Texture2D CreateRuntimeSampleTexture()
    {
        const int size = 256;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "RuntimeVisionSampleTexture"
        };

        Color topLeft = new Color(0.91f, 0.33f, 0.19f, 1f);
        Color topRight = new Color(0.96f, 0.82f, 0.24f, 1f);
        Color bottomLeft = new Color(0.16f, 0.45f, 0.82f, 1f);
        Color bottomRight = new Color(0.17f, 0.71f, 0.48f, 1f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float v = y / (float)(size - 1);
                Color horizontalTop = Color.Lerp(topLeft, topRight, u);
                Color horizontalBottom = Color.Lerp(bottomLeft, bottomRight, u);
                Color baseColor = Color.Lerp(horizontalBottom, horizontalTop, v);

                bool checker = ((x / 32) + (y / 32)) % 2 == 0;
                Color finalColor = checker
                    ? baseColor
                    : Color.Lerp(baseColor, Color.white, 0.18f);

                texture.SetPixel(x, y, finalColor);
            }
        }

        texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return texture;
    }

    private static void CleanupOwnedImage(Object imageSource, bool ownsRuntimeTexture)
    {
        if (!ownsRuntimeTexture || imageSource is not Texture2D texture)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(texture);
        }
        else
        {
            DestroyImmediate(texture);
        }
    }
}
