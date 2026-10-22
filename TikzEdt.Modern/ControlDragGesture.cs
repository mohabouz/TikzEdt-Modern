using System.Windows;

namespace TikzEdt.Modern;

/// <summary>Separates selecting a control from intentionally dragging it.</summary>
internal sealed class ControlDragGesture
{
    private Point _startInViewport;
    private Vector _pointerOffset;
    private bool _started;

    public void Begin(Point pointerInViewport, Point pointerOnCanvas, Point controlCenter)
    {
        _startInViewport = pointerInViewport;
        _pointerOffset = pointerOnCanvas - controlCenter;
        _started = false;
    }

    public bool TryMove(Point pointerInViewport, Point pointerOnCanvas, out Point controlPosition)
    {
        var movement = pointerInViewport - _startInViewport;
        if (!_started && (Math.Abs(movement.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                          Math.Abs(movement.Y) >= SystemParameters.MinimumVerticalDragDistance))
            _started = true;

        controlPosition = pointerOnCanvas - _pointerOffset;
        return _started;
    }
}
