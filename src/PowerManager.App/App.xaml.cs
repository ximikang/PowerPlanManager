using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using PowerManager.App.Services;
using PowerManager.Core.Models;
using PowerManager.Core.Services;
using Windows.Globalization;
using Windows.Storage;

namespace PowerManager.App;

public sealed partial class App : Application
{
    private AppController? _controller;
    private MainWindow? _mainWindow;
    private TrayFlyoutWindow? _trayFlyout;
    private ITrayService? _tray;
    private IStringLocalizer? _localizer;
    private AppInstance? _mainInstance;
    private bool _shuttingDown;

    public App()
    {
        InitializeComponent();
        StartupDiagnostics.Write("App constructed");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupDiagnostics.Write("OnLaunched started");
        try
        {
            _mainInstance = AppInstance.FindOrRegisterForKey("PowerManager.Main");
            StartupDiagnostics.Write($"App instance registered; current={_mainInstance.IsCurrent}");
            if (!_mainInstance.IsCurrent)
            {
                await _mainInstance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
                Environment.Exit(0);
                return;
            }

            _mainInstance.Activated += MainInstance_Activated;
            var settingsPath = Path.Combine(GetLocalStatePath(), "settings.json");
            StartupDiagnostics.Write($"Settings path selected: {settingsPath}");
            var settingsStore = new JsonSettingsStore(settingsPath);
            var initialSettings = await settingsStore.LoadAsync();
            ApplyLanguage(initialSettings.Language);
            StartupDiagnostics.Write("Settings loaded");

            _localizer = new StringLocalizer();
            _controller = new AppController(
                new WindowsPowerPlanService(),
                settingsStore,
                new ScheduleEngine(),
                DispatcherQueue.GetForCurrentThread());
            await _controller.InitializeAsync();
            StartupDiagnostics.Write("Controller initialized");

            var startupService = new StartupService();
            _mainWindow = new MainWindow(_controller, startupService, _localizer);
            StartupDiagnostics.Write("Main window constructed");
            _trayFlyout = new TrayFlyoutWindow(_controller, _localizer);
            _tray = new TrayService(_localizer);
            _tray.Initialize(WinRT.Interop.WindowNative.GetWindowHandle(_mainWindow));
            WireEvents();
            UpdateTray();
            StartupDiagnostics.Write("Tray initialized");

            if (!Environment.GetCommandLineArgs().Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)))
            {
                _mainWindow.ShowWindow();
                StartupDiagnostics.Write("Main window shown");
            }
        }
        catch (Exception exception)
        {
            StartupDiagnostics.Write("OnLaunched failed", exception);
            throw;
        }
    }

    private void WireEvents()
    {
        if (_controller is null || _tray is null || _trayFlyout is null)
        {
            return;
        }

        _controller.StateChanged += (_, _) => UpdateTray();
        _controller.PlanChanged += (_, slot) =>
        {
            if (_controller.Settings.NotificationsEnabled)
            {
                _tray.ShowNotification(
                    _localizer!.Get("AppDisplayName"),
                    string.Format(_localizer.Get("Notification_Switched"), GetSlotName(slot)));
            }
        };
        _tray.SlotInvoked += async (_, slot) =>
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.SelectOrCreateSlotAsync(slot);
            }
        };
        _tray.FlyoutRequested += async (_, _) =>
        {
            try
            {
                await _controller.RefreshAsync();
            }
            finally
            {
                _trayFlyout.ShowNearCursor();
            }
        };
        _tray.OpenRequested += (_, _) => _mainWindow?.ShowWindow();
        _trayFlyout.OpenRequested += (_, _) => _mainWindow?.ShowWindow();
        _trayFlyout.SlotSetupRequested += async (_, slot) =>
        {
            if (_mainWindow is not null)
            {
                await _mainWindow.SelectOrCreateSlotAsync(slot);
            }
        };
        _tray.AutoToggleRequested += async (_, _) =>
        {
            try
            {
                await _controller.SetAutoEnabledAsync(!_controller.Settings.AutoEnabled);
            }
            catch
            {
                _mainWindow?.ShowWindow();
            }
        };
        _tray.SystemStateChanged += async (_, _) =>
        {
            try
            {
                if (_controller.Settings.AutoEnabled)
                {
                    await _controller.ApplyAutomaticAsync();
                }
                else
                {
                    await _controller.RefreshAsync();
                }
            }
            catch
            {
                // The controller raises the actionable error for the main window.
            }
        };
        _tray.ExitRequested += async (_, _) =>
        {
            if (_mainWindow is not null && await _mainWindow.ConfirmExitAsync())
            {
                Shutdown();
            }
        };
    }

    private void MainInstance_Activated(object? sender, AppActivationArguments args)
    {
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _mainWindow?.ShowWindow());
    }

    private void UpdateTray()
    {
        if (_controller is null || _tray is null || _localizer is null)
        {
            return;
        }

        var activePlan = _controller.Plans.FirstOrDefault(plan => plan.Id == _controller.ActivePlanId);
        var availableSlots = _controller.Settings.SlotMappings
            .Where(mapping => mapping.Value is not null && _controller.Plans.Any(plan => plan.Id == mapping.Value.Value))
            .Select(mapping => mapping.Key)
            .ToHashSet();
        var toolTip = $"{_localizer.Get("AppDisplayName")}: {activePlan?.Name ?? _localizer.Get("Plan_Unavailable")}";
        _tray.Update(new TrayState(toolTip, _controller.GetActiveSlot(), _controller.Settings.AutoEnabled, availableSlots));
    }

    private void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _tray?.Dispose();
        _controller?.Dispose();
        _mainWindow?.AllowClose();
        Exit();
    }

    private string GetSlotName(SlotKind slot) => _localizer!.Get(slot switch
    {
        SlotKind.PowerSaver => "Slot_PowerSaver",
        SlotKind.Balanced => "Slot_Balanced",
        SlotKind.HighPerformance => "Slot_HighPerformance",
        SlotKind.UltimatePerformance => "Slot_UltimatePerformance",
        _ => "Plan_Other",
    });

    private static string GetLocalStatePath()
    {
        try
        {
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerPlanManager");
        }
    }

    private static void ApplyLanguage(LanguagePreference preference)
    {
        if (preference == LanguagePreference.System)
        {
            return;
        }

        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = preference switch
            {
                LanguagePreference.English => "en-US",
                LanguagePreference.SimplifiedChinese => "zh-CN",
                _ => string.Empty,
            };
        }
        catch (InvalidOperationException)
        {
            // PrimaryLanguageOverride requires package identity; unpackaged development builds use the Windows language.
        }
    }
}
