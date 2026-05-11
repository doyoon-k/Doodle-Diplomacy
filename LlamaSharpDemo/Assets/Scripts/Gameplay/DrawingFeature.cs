using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class DrawingFeature : IDrawingControlFeature
    {
        private readonly DrawingBoardController _board;
        private readonly DrawingExportBridge _exportBridge;

        public DrawingFeature(DrawingBoardController board, DrawingExportBridge exportBridge)
        {
            _board = board;
            _exportBridge = exportBridge;
        }

        public bool HasVisibleDrawing => _exportBridge != null ? _exportBridge.HasVisibleDrawing : _board != null && _board.HasCanvasMarks;
        public bool IsInteractionLocked => _board != null && _board.IsInteractionLocked;
        public DrawingToolMode CurrentToolMode => _board != null ? _board.GetCurrentToolMode() : DrawingToolMode.Brush;
        public int BrushRadius => _board != null ? _board.BrushRadius : 0;
        public Color BrushColor => _board != null ? _board.BrushColor : Color.black;
        public bool CanUndo => _board != null && _board.CanUndo;
        public bool CanRedo => _board != null && _board.CanRedo;

        public event System.Action<int> BrushRadiusChanged
        {
            add
            {
                if (_board != null)
                {
                    _board.BrushRadiusChanged += value;
                }
            }
            remove
            {
                if (_board != null)
                {
                    _board.BrushRadiusChanged -= value;
                }
            }
        }

        public event System.Action<bool, bool> HistoryStateChanged
        {
            add
            {
                if (_board != null)
                {
                    _board.HistoryStateChanged += value;
                }
            }
            remove
            {
                if (_board != null)
                {
                    _board.HistoryStateChanged -= value;
                }
            }
        }

        public void EnsureRuntimeEnabled()
        {
            if (_board != null)
            {
                _board.enabled = true;
            }
        }

        public void ClearCanvas() => _board?.ClearCanvas();
        public void SetInteractionLocked(bool locked) => _board?.SetInteractionLocked(locked);
        public void SetToolMode(DrawingToolMode mode) => _board?.SetToolMode(mode);
        public void SetBrushRadius(float radius) => _board?.SetBrushRadius(radius);
        public void SetBrushColor(Color color) => _board?.SetBrushColor(color);
        public bool Undo() => _board != null && _board.Undo();
        public bool Redo() => _board != null && _board.Redo();

        public bool TryExportPngBytes(out byte[] pngBytes, out string error)
        {
            if (_exportBridge == null)
            {
                pngBytes = null;
                error = "DrawingExportBridge reference is missing.";
                return false;
            }

            return _exportBridge.TryExportPngBytes(out pngBytes, out error);
        }

        public bool TryExportPngBase64(out string base64Png, out string error)
        {
            if (_exportBridge == null)
            {
                base64Png = null;
                error = "DrawingExportBridge reference is missing.";
                return false;
            }

            return _exportBridge.TryExportPngBase64(out base64Png, out error);
        }
    }
}
