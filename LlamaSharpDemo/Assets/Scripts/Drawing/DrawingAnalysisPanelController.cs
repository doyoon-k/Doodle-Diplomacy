using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runs the configured drawing analysis pipeline and renders status/results into a small UGUI panel.
/// </summary>
public class DrawingAnalysisPanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private DrawingExportBridge exportBridge;
    [SerializeField] private Button analyzeButton;
    [SerializeField] private Text analyzeButtonText;
    [SerializeField] private Image analyzeButtonImage;
    [SerializeField] private Text analysisStatusText;
    [SerializeField] private Text analysisResultText;

    [Header("Theme")]
    [SerializeField] private Color buttonReadyColor = new(0.18f, 0.46f, 0.78f, 1f);
    [SerializeField] private Color buttonBusyColor = new(0.72f, 0.52f, 0.16f, 1f);
    [SerializeField] private Color buttonDisabledColor = new(0.18f, 0.20f, 0.24f, 0.8f);

    private bool _isAnalyzing;

    private void Awake()
    {
        if (exportBridge == null)
        {
            exportBridge = FindFirstObjectByType<DrawingExportBridge>();
        }

        CacheReferences();
        BindControls();
        SetIdleMessage();
        RefreshButtonState();
    }

    private void CacheReferences()
    {
        if (analyzeButton == null)
        {
            analyzeButton = FindNamedComponent<Button>("AnalyzeDrawingButton");
        }

        if (analyzeButtonText == null)
        {
            analyzeButtonText = FindNamedComponent<Text>("AnalyzeDrawingButtonText");
        }

        if (analyzeButtonImage == null && analyzeButton != null)
        {
            analyzeButtonImage = analyzeButton.GetComponent<Image>();
        }

        if (analysisStatusText == null)
        {
            analysisStatusText = FindNamedComponent<Text>("AnalysisStatusText");
        }

        if (analysisResultText == null)
        {
            analysisResultText = FindNamedComponent<Text>("AnalysisResultText");
        }
    }

    private void BindControls()
    {
        if (analyzeButton == null)
        {
            return;
        }

        analyzeButton.onClick.RemoveListener(OnAnalyzeButtonClicked);
        analyzeButton.onClick.AddListener(OnAnalyzeButtonClicked);
    }

    private void OnAnalyzeButtonClicked()
    {
        if (_isAnalyzing)
        {
            return;
        }

        if (exportBridge == null)
        {
            SetFailureMessage("Drawing export bridge is missing.");
            return;
        }

        _isAnalyzing = true;
        if (analysisStatusText != null)
        {
            analysisStatusText.text = "Analyzing drawing...";
        }

        if (analysisResultText != null)
        {
            analysisResultText.text = "Vision model is processing the current board texture.";
        }

        RefreshButtonState();

        if (!exportBridge.TryRunConfiguredPipeline(OnPipelineCompleted, out string error))
        {
            _isAnalyzing = false;
            SetFailureMessage(error);
            RefreshButtonState();
        }
    }

    private void OnPipelineCompleted(PipelineState finalState)
    {
        _isAnalyzing = false;

        if (finalState == null)
        {
            SetFailureMessage("Pipeline returned no final state.");
            RefreshButtonState();
            return;
        }

        if (finalState.TryGetString(PromptPipelineConstants.ErrorKey, out string pipelineError) &&
            !string.IsNullOrWhiteSpace(pipelineError))
        {
            SetFailureMessage(pipelineError);
            RefreshButtonState();
            return;
        }

        if (analysisStatusText != null)
        {
            analysisStatusText.text = "Analysis complete.";
        }

        if (analysisResultText != null)
        {
            analysisResultText.text = GetResultSummary(finalState);
        }

        RefreshButtonState();
    }

    private void SetIdleMessage()
    {
        if (analysisStatusText != null)
        {
            analysisStatusText.text = "Ready. Draw something, then analyze it.";
        }

        if (analysisResultText != null)
        {
            analysisResultText.text = "VLM output will appear here.";
        }
    }

    private void SetFailureMessage(string error)
    {
        if (analysisStatusText != null)
        {
            analysisStatusText.text = "Analysis failed.";
        }

        if (analysisResultText != null)
        {
            analysisResultText.text = string.IsNullOrWhiteSpace(error)
                ? "Unknown pipeline error."
                : error;
        }
    }

    private void RefreshButtonState()
    {
        bool canAnalyze = exportBridge != null && !_isAnalyzing;
        if (analyzeButton != null)
        {
            analyzeButton.interactable = canAnalyze;
        }

        if (analyzeButtonText != null)
        {
            analyzeButtonText.text = _isAnalyzing ? "Analyzing..." : "Analyze Drawing";
        }

        if (analyzeButtonImage != null)
        {
            analyzeButtonImage.color = _isAnalyzing
                ? buttonBusyColor
                : canAnalyze
                    ? buttonReadyColor
                    : buttonDisabledColor;
        }
    }

    private static string GetResultSummary(PipelineState state)
    {
        if (state == null)
        {
            return "No analysis output.";
        }

        if (state.TryGetString("image_summary", out string imageSummary) &&
            !string.IsNullOrWhiteSpace(imageSummary))
        {
            return imageSummary;
        }

        if (state.TryGetString(PromptPipelineConstants.AnswerKey, out string answer) &&
            !string.IsNullOrWhiteSpace(answer))
        {
            return answer;
        }

        var builder = new StringBuilder();
        foreach (string key in state.AllKeys)
        {
            if (string.Equals(key, "analysis_goal", System.StringComparison.Ordinal) ||
                string.Equals(key, "reference_image", System.StringComparison.Ordinal) ||
                string.Equals(key, PromptPipelineConstants.ErrorKey, System.StringComparison.Ordinal))
            {
                continue;
            }

            string displayValue = state.GetDisplayValue(key);
            if (string.IsNullOrWhiteSpace(displayValue))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(key);
            builder.Append(": ");
            builder.Append(displayValue);
        }

        return builder.Length > 0 ? builder.ToString() : "The pipeline completed but returned no readable summary.";
    }

    private T FindNamedComponent<T>(string objectName) where T : Component
    {
        T[] components = GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].name == objectName)
            {
                return components[i];
            }
        }

        return null;
    }
}
