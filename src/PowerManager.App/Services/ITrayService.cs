using PowerManager.Core.Models;

namespace PowerManager.App.Services;

public interface ITrayService : IDisposable
{
    event EventHandler<SlotKind>? SlotInvoked;

    event EventHandler? FlyoutRequested;

    event EventHandler? OpenRequested;

    event EventHandler? AutoToggleRequested;

    event EventHandler? ExitRequested;

    event EventHandler? SystemStateChanged;

    void Initialize(nint windowHandle);

    void Update(TrayState state);

    void ShowNotification(string title, string message);
}

public sealed record TrayState(string ToolTip, SlotKind? ActiveSlot, bool AutoEnabled, IReadOnlySet<SlotKind> AvailableSlots);
