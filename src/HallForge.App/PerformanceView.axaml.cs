using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HallForge.App;

public partial class PerformanceView : UserControl
{
    private KeyModifiers? _pointerModifiers;

    public PerformanceView()
    {
        InitializeComponent();
        // Buttons handle the pointer themselves, so the modifiers are captured on the way down.
        KeyboardView.AddHandler(PointerPressedEvent, (_, e) => _pointerModifiers = e.KeyModifiers, RoutingStrategies.Tunnel, handledEventsToo: true);
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
