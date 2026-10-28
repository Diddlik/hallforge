using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HallForge.App;

public partial class PerformanceView : UserControl
{
    // Movement below this distance is still a click, so a slightly shaky click does not start a drag selection.
    private const double DragThreshold = 6;

    private KeyModifiers? _pointerModifiers;
    private Point? _dragStart;
    private bool _dragging;

    public PerformanceView()
    {
        InitializeComponent();
        // Keys are buttons that handle the pointer themselves, so the area listens on the way down.
        KeyboardArea.AddHandler(PointerPressedEvent, OnAreaPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyboardArea.AddHandler(PointerMovedEvent, OnAreaMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        KeyboardArea.AddHandler(PointerReleasedEvent, OnAreaReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        _pointerModifiers = e.KeyModifiers;
        if (e.GetCurrentPoint(KeyboardArea).Properties.IsLeftButtonPressed)
            _dragStart = e.GetPosition(KeyboardArea);
    }

    private void OnAreaMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start)
            return;
        var position = e.GetPosition(KeyboardArea);
        if (!_dragging)
        {
            if (Math.Abs(position.X - start.X) < DragThreshold && Math.Abs(position.Y - start.Y) < DragThreshold)
                return;
            _dragging = true;
            // Taking the capture from the key button cancels its click.
            e.Pointer.Capture(KeyboardArea);
            Band.IsVisible = true;
        }
        var rect = new Rect(start, position).Normalize();
        Canvas.SetLeft(Band, rect.X);
        Canvas.SetTop(Band, rect.Y);
        Band.Width = rect.Width;
        Band.Height = rect.Height;
        e.Handled = true;
    }

    private void OnAreaReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _dragStart is { } start && DataContext is KeyboardEditorViewModel editor)
        {
            var rect = new Rect(start, e.GetPosition(KeyboardArea)).Normalize();
            var add = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            editor.SelectArea(rect.X, rect.Y, rect.Width, rect.Height, add);
            e.Pointer.Capture(null);
            e.Handled = true;
            _pointerModifiers = null;
        }
        Band.IsVisible = false;
        _dragStart = null;
        _dragging = false;
    }

    private void OnKeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: KeyViewModel key } || DataContext is not KeyboardEditorViewModel editor)
            return;
        // Space or Enter on a focused key toggles it, so the keyboard alone can build a multi-key selection.
        var modifiers = _pointerModifiers;
        _pointerModifiers = null;
        if (modifiers is not { } m)
            editor.Pick(key, toggle: true, extend: false);
        else
            editor.Pick(key, toggle: m.HasFlag(KeyModifiers.Control) || m.HasFlag(KeyModifiers.Meta), extend: m.HasFlag(KeyModifiers.Shift));
    }
}
