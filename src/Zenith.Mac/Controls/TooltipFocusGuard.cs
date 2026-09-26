using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Zenith.Mac;

/// <summary>Keep hover tips tied to the active native window.</summary>
internal static class TooltipFocusGuard
{
    static readonly ConditionalWeakTable<Window, object> attached = new();

    public static void Attach(Window window)
    {
        if (attached.TryGetValue(window, out _)) return;
        attached.Add(window, new object());

        window.Activated += (_, _) => ToolTip.SetServiceEnabled(window, true);
        window.Deactivated += (_, _) =>
        {
            // Close an already visible tip before disabling the inheritable
            // service property for this window's controls.
            foreach (var control in window.GetVisualDescendants().OfType<Control>())
                if (ToolTip.GetIsOpen(control)) ToolTip.SetIsOpen(control, false);
            if (ToolTip.GetIsOpen(window)) ToolTip.SetIsOpen(window, false);
            ToolTip.SetServiceEnabled(window, false);
        };
        window.Opened += (_, _) => ToolTip.SetServiceEnabled(window, window.IsActive);
    }
}
