using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerManager.App.Native;
using PowerManager.App.Services;
using PowerManager.Core.Models;
using Windows.Graphics;

namespace PowerManager.App;

public sealed partial class TrayFlyoutWindow : Window
{
    private readonly AppController _controller;
    private readonly IStringLocalizer _localizer;
    private readonly AppWindow _appWindow;
    private bool _updating;

    public TrayFlyoutWindow(AppController controller, IStringLocalizer localizer)
    {
        _controller = controller;
        _localizer = localizer;
        InitializeComponent();
        Title = _localizer.Get("AppDisplayName");

        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle));
        _appWindow.Resize(new SizeInt32(380, 340));
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        Activated += TrayFlyoutWindow_Activated;
        _controller.StateChanged += (_, _) => RefreshUi();
        RefreshUi();
    }

    public event EventHandler? OpenRequested;
    public event EventHandler<SlotKind>? SlotSetupRequested;

    public void ShowNearCursor()
    {
        TrayNativeMethods.GetCursorPos(out var point);
        var displayArea = DisplayArea.GetFromPoint(new PointInt32(point.X, point.Y), DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var x = Math.Clamp(point.X - 370, workArea.X, workArea.X + workArea.Width - 380);
        var y = Math.Clamp(point.Y - 350, workArea.Y, workArea.Y + workArea.Height - 340);
        _appWindow.MoveAndResize(new RectInt32(x, y, 380, 340));
        RefreshUi();
        Activate();
        _appWindow.Show();
    }

    public void Hide() => _appWindow.Hide();

    private void TrayFlyoutWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            Hide();
        }
    }

    private void RefreshUi()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RefreshUi);
            return;
        }

        _updating = true;
        var activePlan = _controller.Plans.FirstOrDefault(plan => plan.Id == _controller.ActivePlanId);
        CurrentPlanText.Text = activePlan?.Name ?? _localizer.Get("Plan_Unavailable");
        NextSwitchText.Text = _controller.CurrentDecision?.NextBoundary is { } boundary
            ? string.Format(_localizer.Get("NextSwitch_Format"), boundary.LocalDateTime)
            : _localizer.Get("NextSwitch_None");
        TrayAutoToggle.IsOn = _controller.Settings.AutoEnabled;
        var activeSlot = _controller.GetActiveSlot();
        UpdateButton(PowerSaverButton, SlotKind.PowerSaver, activeSlot);
        UpdateButton(BalancedButton, SlotKind.Balanced, activeSlot);
        UpdateButton(HighPerformanceButton, SlotKind.HighPerformance, activeSlot);
        UpdateButton(UltimatePerformanceButton, SlotKind.UltimatePerformance, activeSlot);
        _updating = false;
    }

    private void UpdateButton(Button button, SlotKind slot, SlotKind? activeSlot)
    {
        var available = _controller.Settings.SlotMappings.TryGetValue(slot, out var id)
            && id is not null
            && _controller.Plans.Any(plan => plan.Id == id.Value);
        button.IsEnabled = true;
        button.Opacity = activeSlot == slot ? 1 : available ? 0.74 : 0.55;
    }

    private async void QuickSlotButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string value } && Enum.TryParse<SlotKind>(value, out var slot))
        {
            var available = _controller.Settings.SlotMappings.TryGetValue(slot, out var id)
                && id is not null
                && _controller.Plans.Any(plan => plan.Id == id.Value);
            if (!available)
            {
                Hide();
                SlotSetupRequested?.Invoke(this, slot);
                return;
            }

            try
            {
                await _controller.SwitchSlotAsync(slot);
            }
            catch
            {
                // The main window receives the localized controller error.
            }
            finally
            {
                Hide();
            }
        }
    }

    private async void TrayAutoToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_updating)
        {
            return;
        }

        try
        {
            await _controller.SetAutoEnabledAsync(TrayAutoToggle.IsOn);
        }
        catch
        {
            RefreshUi();
        }
    }

    private void OpenButton_Click(object sender, RoutedEventArgs args)
    {
        Hide();
        OpenRequested?.Invoke(this, EventArgs.Empty);
    }
}
