using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal sealed class PromptPipelineCommandManager
{
    private readonly Func<PromptPipelineAsset> _assetProvider;
    private readonly Action _refreshView;
    private readonly Action _markDirty;
    private readonly Stack<PipelineCommand> _undoStack = new();
    private readonly Stack<PipelineCommand> _redoStack = new();

    public PromptPipelineCommandManager(Func<PromptPipelineAsset> assetProvider, Action refreshView, Action markDirty)
    {
        _assetProvider = assetProvider;
        _refreshView = refreshView;
        _markDirty = markDirty;
    }

    public void Execute(string label, Action mutate)
    {
        var asset = _assetProvider?.Invoke();
        if (asset == null || mutate == null)
        {
            return;
        }

        var before = PipelineSnapshot.Capture(asset);
        mutate();
        var after = PipelineSnapshot.Capture(asset);

        if (before.Equals(after))
        {
            return;
        }

        _undoStack.Push(new PipelineCommand(label, before, after));
        _redoStack.Clear();
        _markDirty?.Invoke();
    }

    public bool Undo()
    {
        var asset = _assetProvider?.Invoke();
        if (asset == null || _undoStack.Count == 0)
        {
            return false;
        }

        var command = _undoStack.Pop();
        command.Undo(asset);
        _redoStack.Push(command);
        _markDirty?.Invoke();
        _refreshView?.Invoke();
        return true;
    }

    public bool Redo()
    {
        var asset = _assetProvider?.Invoke();
        if (asset == null || _redoStack.Count == 0)
        {
            return false;
        }

        var command = _redoStack.Pop();
        command.Redo(asset);
        _undoStack.Push(command);
        _markDirty?.Invoke();
        _refreshView?.Invoke();
        return true;
    }

    public void Reset()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}

internal sealed class PipelineCommand
{
    public string Label { get; }
    private readonly PipelineSnapshot _before;
    private readonly PipelineSnapshot _after;

    public PipelineCommand(string label, PipelineSnapshot before, PipelineSnapshot after)
    {
        Label = label;
        _before = before;
        _after = after;
    }

    public void Undo(PromptPipelineAsset asset) => _before.ApplyTo(asset);
    public void Redo(PromptPipelineAsset asset) => _after.ApplyTo(asset);
}

internal sealed class PipelineSnapshot : IEquatable<PipelineSnapshot>
{
    private readonly string _displayName;
    private readonly string _description;
    private readonly List<PromptPipelineStep> _steps;
    private readonly PromptPipelineLayoutSettings _layoutSettings;

    private PipelineSnapshot(string displayName, string description, List<PromptPipelineStep> steps, PromptPipelineLayoutSettings layout)
    {
        _displayName = displayName;
        _description = description;
        _steps = steps;
        _layoutSettings = layout;
    }

    public static PipelineSnapshot Capture(PromptPipelineAsset asset)
    {
        return new PipelineSnapshot(
            asset.displayName,
            asset.description,
            CloneSteps(asset.steps),
            CloneLayout(asset.layoutSettings)
        );
    }

    public void ApplyTo(PromptPipelineAsset asset)
    {
        asset.displayName = _displayName;
        asset.description = _description;
        asset.steps = CloneSteps(_steps);
        asset.layoutSettings = CloneLayout(_layoutSettings);
    }

    public bool Equals(PipelineSnapshot other)
    {
        if (other == null)
        {
            return false;
        }

        if (!string.Equals(_displayName, other._displayName, StringComparison.Ordinal) ||
            !string.Equals(_description, other._description, StringComparison.Ordinal))
        {
            return false;
        }

        if (!LayoutEquals(_layoutSettings, other._layoutSettings))
        {
            return false;
        }

        if (_steps == null && other._steps == null)
        {
            return true;
        }

        if (_steps == null || other._steps == null || _steps.Count != other._steps.Count)
        {
            return false;
        }

        for (int i = 0; i < _steps.Count; i++)
        {
            if (!StepEquals(_steps[i], other._steps[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object obj) => Equals(obj as PipelineSnapshot);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = (_displayName?.GetHashCode() ?? 0) ^ (_description?.GetHashCode() ?? 0);
            if (_layoutSettings != null)
            {
                hash = (hash * 397) ^ _layoutSettings.viewPosition.GetHashCode();
                hash = (hash * 397) ^ _layoutSettings.viewScale.GetHashCode();
            }
            hash = (hash * 397) ^ (_steps?.Count ?? 0);
            return hash;
        }
    }

    private static List<PromptPipelineStep> CloneSteps(List<PromptPipelineStep> source)
    {
        if (source == null)
        {
            return new List<PromptPipelineStep>();
        }

        return source.Select(CloneStep).ToList();
    }

    private static PromptPipelineStep CloneStep(PromptPipelineStep step)
    {
        if (step == null)
        {
            return null;
        }

        return new PromptPipelineStep
        {
            stepName = step.stepName,
            stepKind = step.stepKind,
            llmProfile = step.llmProfile,
            customAsset = step.customAsset,
            userPromptTemplate = step.userPromptTemplate,
            jsonMaxRetries = step.jsonMaxRetries,
            jsonRetryDelaySeconds = step.jsonRetryDelaySeconds,
            customLinkTypeName = step.customLinkTypeName,
            customLinkParameters = CloneCustomParams(step.customLinkParameters),
            editorPosition = step.editorPosition,
            guid = step.guid,
            nextStepGuid = step.nextStepGuid
        };
    }

    private static List<CustomLinkParameter> CloneCustomParams(List<CustomLinkParameter> source)
    {
        if (source == null)
        {
            return new List<CustomLinkParameter>();
        }

        return source
            .Select(p => p == null
                ? null
                : new CustomLinkParameter { key = p.key, value = p.value })
            .ToList();
    }

    private static PromptPipelineLayoutSettings CloneLayout(PromptPipelineLayoutSettings source)
    {
        if (source == null)
        {
            return new PromptPipelineLayoutSettings();
        }

        return new PromptPipelineLayoutSettings
        {
            inputNodePosition = source.inputNodePosition,
            outputNodePosition = source.outputNodePosition,
            inputPositionInitialized = source.inputPositionInitialized,
            outputPositionInitialized = source.outputPositionInitialized,
            viewPosition = source.viewPosition,
            viewScale = source.viewScale,
            viewInitialized = source.viewInitialized,
            snapshotPositions = source.snapshotPositions != null
                ? new List<Vector2>(source.snapshotPositions)
                : new List<Vector2>(),
            snapshotPositionsInitialized = source.snapshotPositionsInitialized
        };
    }

    private static bool StepEquals(PromptPipelineStep a, PromptPipelineStep b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (!string.Equals(a.stepName, b.stepName, StringComparison.Ordinal) ||
            a.stepKind != b.stepKind ||
            !ReferenceEquals(a.llmProfile, b.llmProfile) ||
            !ReferenceEquals(a.customAsset, b.customAsset) ||
            !string.Equals(a.userPromptTemplate, b.userPromptTemplate, StringComparison.Ordinal) ||
            a.jsonMaxRetries != b.jsonMaxRetries ||
            Math.Abs(a.jsonRetryDelaySeconds - b.jsonRetryDelaySeconds) > 0.0001f ||
            !string.Equals(a.customLinkTypeName, b.customLinkTypeName, StringComparison.Ordinal) ||
            !string.Equals(a.guid, b.guid, StringComparison.Ordinal) ||
            !string.Equals(a.nextStepGuid, b.nextStepGuid, StringComparison.Ordinal) ||
            (a.customLinkParameters?.Count ?? 0) != (b.customLinkParameters?.Count ?? 0) ||
            (a.editorPosition - b.editorPosition).sqrMagnitude > 0.0001f)
        {
            return false;
        }

        if (a.customLinkParameters != null)
        {
            for (int i = 0; i < a.customLinkParameters.Count; i++)
            {
                var pa = a.customLinkParameters[i];
                var pb = b.customLinkParameters[i];
                if (pa == null && pb == null)
                {
                    continue;
                }

                if (pa == null || pb == null ||
                    !string.Equals(pa.key, pb.key, StringComparison.Ordinal) ||
                    !string.Equals(pa.value, pb.value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool LayoutEquals(PromptPipelineLayoutSettings a, PromptPipelineLayoutSettings b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if ((a.inputNodePosition - b.inputNodePosition).sqrMagnitude > 0.0001f ||
            (a.outputNodePosition - b.outputNodePosition).sqrMagnitude > 0.0001f ||
            a.inputPositionInitialized != b.inputPositionInitialized ||
            a.outputPositionInitialized != b.outputPositionInitialized ||
            (a.viewPosition - b.viewPosition).sqrMagnitude > 0.0001f ||
            (a.viewScale - b.viewScale).sqrMagnitude > 0.0001f ||
            a.viewInitialized != b.viewInitialized ||
            a.snapshotPositionsInitialized != b.snapshotPositionsInitialized)
        {
            return false;
        }

        if ((a.snapshotPositions?.Count ?? 0) != (b.snapshotPositions?.Count ?? 0))
        {
            return false;
        }

        if (a.snapshotPositions != null)
        {
            for (int i = 0; i < a.snapshotPositions.Count; i++)
            {
                if ((a.snapshotPositions[i] - b.snapshotPositions[i]).sqrMagnitude > 0.0001f)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
