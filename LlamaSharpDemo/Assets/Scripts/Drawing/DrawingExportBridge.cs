using System;
using UnityEngine;

/// <summary>
/// Exports the current drawing board image on demand and can push it into a configured vision pipeline.
/// </summary>
public class DrawingExportBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DrawingBoardController drawingBoard;
    [SerializeField] private PromptPipelineAsset pipelineAsset;

    [Header("Pipeline Keys")]
    [SerializeField] private string imageStateKey = "reference_image";
    [SerializeField] private string analysisGoalKey = "analysis_goal";
    [TextArea(2, 4)]
    [SerializeField] private string analysisGoal = "Analyze the player's drawing.";

    [Header("Debug")]
    [SerializeField] private bool logExports = true;
    [SerializeField] private bool logPipelineCompletion = true;

    public event Action<PipelineState> PipelineCompleted;

    public Texture2D CurrentTexture => drawingBoard != null ? drawingBoard.GetCompositeTextureForExport() : null;
    public PromptPipelineAsset PipelineAsset => pipelineAsset;
    public string ImageStateKey => imageStateKey;

    private void Awake()
    {
        if (drawingBoard == null)
        {
            drawingBoard = GetComponent<DrawingBoardController>();
        }
    }

    public bool TryGetCurrentTexture(out Texture2D texture, out string error)
    {
        texture = CurrentTexture;
        error = null;

        if (drawingBoard == null)
        {
            error = "DrawingBoardController reference is missing.";
            return false;
        }

        if (texture == null)
        {
            error = "Drawing board texture is not available yet.";
            return false;
        }

        return true;
    }

    public bool TryExportPngBytes(out byte[] pngBytes, out string error)
    {
        pngBytes = null;

        if (!TryGetCurrentTexture(out Texture2D texture, out error))
        {
            return false;
        }

        bool encoded = PipelineImageUtility.TryEncodeToPng(texture, out pngBytes, out error);
        if (encoded && logExports)
        {
            Debug.Log($"[DrawingExportBridge] Exported PNG ({pngBytes.Length} bytes) from {texture.width}x{texture.height} drawing.");
        }

        return encoded;
    }

    public bool TryExportPngBase64(out string base64Png, out string error)
    {
        base64Png = null;

        if (!TryExportPngBytes(out byte[] pngBytes, out error))
        {
            return false;
        }

        base64Png = Convert.ToBase64String(pngBytes);
        if (logExports)
        {
            Debug.Log($"[DrawingExportBridge] Encoded PNG to base64 ({base64Png.Length} chars).");
        }

        return true;
    }

    public PipelineState CreatePipelineState(string overrideAnalysisGoal = null)
    {
        if (!TryGetCurrentTexture(out Texture2D texture, out string error))
        {
            throw new InvalidOperationException(error);
        }

        var state = new PipelineState();
        state.SetImage(GetResolvedImageStateKey(), texture);

        string resolvedGoal = overrideAnalysisGoal ?? analysisGoal;
        if (!string.IsNullOrWhiteSpace(analysisGoalKey) && !string.IsNullOrWhiteSpace(resolvedGoal))
        {
            state.SetString(analysisGoalKey, resolvedGoal);
        }

        return state;
    }

    public bool TryRunConfiguredPipeline(Action<PipelineState> onComplete, out string error, string overrideAnalysisGoal = null)
    {
        error = null;

        if (pipelineAsset == null)
        {
            error = "PromptPipelineAsset reference is missing.";
            return false;
        }

        if (GamePipelineRunner.Instance == null)
        {
            error = "GamePipelineRunner.Instance is missing.";
            return false;
        }

        PipelineState state;
        try
        {
            state = CreatePipelineState(overrideAnalysisGoal);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (logExports)
        {
            Debug.Log($"[DrawingExportBridge] Running '{pipelineAsset.name}' with {PipelineState.DescribeValue(CurrentTexture)}");
        }

        GamePipelineRunner.Instance.RunPipeline(pipelineAsset, state, finalState =>
        {
            if (logPipelineCompletion)
            {
                Debug.Log($"[DrawingExportBridge] Pipeline '{pipelineAsset.name}' completed.");
            }

            PipelineCompleted?.Invoke(finalState);
            onComplete?.Invoke(finalState);
        });

        return true;
    }

    [ContextMenu("Export/Log PNG Export")]
    private void LogPngExport()
    {
        if (!TryExportPngBytes(out _, out string error))
        {
            Debug.LogError($"[DrawingExportBridge] {error}");
        }
    }

    [ContextMenu("Pipeline/Run Configured Pipeline")]
    private void RunConfiguredPipelineFromContextMenu()
    {
        if (!TryRunConfiguredPipeline(null, out string error))
        {
            Debug.LogError($"[DrawingExportBridge] {error}");
        }
    }

    private string GetResolvedImageStateKey()
    {
        return string.IsNullOrWhiteSpace(imageStateKey) ? "reference_image" : imageStateKey.Trim();
    }
}
