using DoodleDiplomacy.Gameplay;

namespace DoodleDiplomacy.Devices
{
    internal static class TabletControlCommands
    {
        public const int SmallBrushSize = 3;
        public const int MediumBrushSize = 6;
        public const int LargeBrushSize = 12;

        public static bool TryExecute(TabletControlTarget target, IDrawingControlFeature drawingControls)
        {
            if (drawingControls == null)
            {
                return false;
            }

            switch (target)
            {
                case TabletControlTarget.Brush:
                    drawingControls.SetToolMode(DrawingToolMode.Brush);
                    return true;
                case TabletControlTarget.Fill:
                    drawingControls.SetToolMode(DrawingToolMode.Fill);
                    return true;
                case TabletControlTarget.Eraser:
                    drawingControls.SetToolMode(DrawingToolMode.Eraser);
                    return true;
                case TabletControlTarget.SizeSmall:
                    drawingControls.SetBrushRadius(SmallBrushSize);
                    return true;
                case TabletControlTarget.SizeMedium:
                    drawingControls.SetBrushRadius(MediumBrushSize);
                    return true;
                case TabletControlTarget.SizeLarge:
                    drawingControls.SetBrushRadius(LargeBrushSize);
                    return true;
                case TabletControlTarget.Undo:
                    drawingControls.Undo();
                    return true;
                case TabletControlTarget.Redo:
                    drawingControls.Redo();
                    return true;
                case TabletControlTarget.Clear:
                    drawingControls.ClearCanvas();
                    return true;
                default:
                    return false;
            }
        }
    }
}
