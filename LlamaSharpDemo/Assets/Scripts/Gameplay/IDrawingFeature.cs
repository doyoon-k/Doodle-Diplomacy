using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public interface IDrawingFeature
    {
        bool HasVisibleDrawing { get; }
        bool IsInteractionLocked { get; }
        DrawingToolMode CurrentToolMode { get; }

        void EnsureRuntimeEnabled();
        void ClearCanvas();
        void SetInteractionLocked(bool locked);
        void SetToolMode(DrawingToolMode mode);
        void SetBrushRadius(float radius);
        void SetBrushColor(Color color);
        void ShowRecognitionLabel(string label);
        void ClearRecognitionLabel();
        bool Undo();
        bool Redo();
        bool TryExportPngBytes(out byte[] pngBytes, out string error);
        bool TryExportPngBase64(out string base64Png, out string error);
    }
}
