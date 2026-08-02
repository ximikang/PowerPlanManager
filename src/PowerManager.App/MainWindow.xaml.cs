using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerManager.App.Services;
using PowerManager.Core.Models;
using PowerManager.Core.Services;
using Windows.Globalization;
using Windows.Graphics;

namespace PowerManager.App;

public sealed partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly IStartupService _startupService;
    private readonly IStringLocalizer _localizer;
    private readonly AppWindow _appWindow;
    private bool _updatingUi;
    private bool _allowClose;
    private bool _firstRunDialogShown;

    public MainWindow(AppController controller, IStartupService startupService, IStringLocalizer localizer)
    {
        _controller = controller;
        _startupService = startupService;
        _localizer = localizer;
        InitializeComponent();
        Title = _localizer.Get("AppDisplayName");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Resize(new SizeInt32(980, 860));
        _appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PowerManager.ico"));
        _appWindow.Closing += AppWindow_Closing;

        _controller.StateChanged += Controller_StateChanged;
        _controller.ErrorOccurred += Controller_ErrorOccurred;
        Activated += MainWindow_Activated;
        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.Loaded += MainContent_Loaded;
        }

        InitializeStaticChoices();
        RefreshUi();
    }

    public void ShowWindow()
    {
        _appWindow.Show();
        Activate();
    }

    public void HideWindow() => _appWindow.Hide();

    public void AllowClose() => _allowClose = true;

    public async Task SelectOrCreateSlotAsync(SlotKind slot)
    {
        var available = _controller.Settings.SlotMappings.TryGetValue(slot, out var planId)
            && planId is not null
            && _controller.Plans.Any(plan => plan.Id == planId.Value);
        if (available)
        {
            try
            {
                await _controller.SwitchSlotAsync(slot);
            }
            catch
            {
                // The controller reports a localized error through ErrorOccurred.
            }

            return;
        }

        ShowWindow();
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = string.Format(_localizer.Get("CreateSlot_Title"), GetSlotName(slot)),
            Content = _localizer.Get("CreateSlot_Text"),
            PrimaryButtonText = _localizer.Get("Create_Confirm"),
            CloseButtonText = _localizer.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await _controller.CreateAndBindPlanAsync(slot);
            await _controller.SwitchSlotAsync(slot);
            ShowStatus(_localizer.Get("Status_Created"), InfoBarSeverity.Success);
        }
        catch
        {
            // The controller reports a localized error through ErrorOccurred.
        }
    }

    public async Task<bool> ConfirmExitAsync()
    {
        ShowWindow();
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _localizer.Get("Exit_Title"),
            Content = _localizer.Get("Exit_Text"),
            PrimaryButtonText = _localizer.Get("Exit_Confirm"),
            CloseButtonText = _localizer.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void InitializeStaticChoices()
    {
        var timeChoices = Enumerable.Range(0, 24 * 12)
            .Select(index =>
            {
                var value = new TimeOnly(index / 12, index % 12 * 5);
                return new TimeChoice(value, value.ToString("HH:mm"));
            })
            .ToArray();
        StartTimeCombo.ItemsSource = timeChoices;
        EndTimeCombo.ItemsSource = timeChoices;
        StartTimeCombo.SelectedItem = timeChoices.First(choice => choice.Value == new TimeOnly(12, 0));
        EndTimeCombo.SelectedItem = timeChoices.First(choice => choice.Value == new TimeOnly(14, 0));

        RuleTargetCombo.ItemsSource = GetSlotChoices();
        RuleTargetCombo.SelectedIndex = 1;
        DefaultSlotCombo.ItemsSource = GetSlotChoices();
        LanguageCombo.ItemsSource = new[]
        {
            new LanguageChoice(LanguagePreference.System, _localizer.Get("Language_System")),
            new LanguageChoice(LanguagePreference.English, _localizer.Get("Language_English")),
            new LanguageChoice(LanguagePreference.SimplifiedChinese, _localizer.Get("Language_Chinese")),
        };
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        try
        {
            await _controller.RefreshAsync();
            await RefreshStartupStateAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void MainContent_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement contentRoot)
        {
            contentRoot.Loaded -= MainContent_Loaded;
        }
        if (_controller.Settings.FirstRunCompleted || _firstRunDialogShown || Content.XamlRoot is null)
        {
            return;
        }

        _firstRunDialogShown = true;
        try
        {
            await ShowFirstRunDialogAsync();
        }
        catch (Exception exception)
        {
            _firstRunDialogShown = false;
            ShowError(exception);
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private void Controller_StateChanged(object? sender, EventArgs args)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RefreshUi);
            return;
        }

        RefreshUi();
    }

    private void Controller_ErrorOccurred(object? sender, Exception exception)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowError(exception));
            return;
        }

        ShowError(exception);
    }

    private void RefreshUi()
    {
        _updatingUi = true;
        try
        {
            var planChoices = new List<PlanChoice>
            {
                new(null, _localizer.Get("Plan_Unmapped")),
            };
            planChoices.AddRange(_controller.Plans.Select(plan => new PlanChoice(plan.Id, plan.Name)));

            SetMappingItems(PowerSaverMapping, planChoices, SlotKind.PowerSaver);
            SetMappingItems(BalancedMapping, planChoices, SlotKind.Balanced);
            SetMappingItems(HighPerformanceMapping, planChoices, SlotKind.HighPerformance);
            SetMappingItems(UltimatePerformanceMapping, planChoices, SlotKind.UltimatePerformance);

            DefaultSlotCombo.SelectedItem = ((IEnumerable<SlotChoice>)DefaultSlotCombo.ItemsSource)
                .First(choice => choice.Kind == _controller.Settings.DefaultSlot);
            LanguageCombo.SelectedItem = ((IEnumerable<LanguageChoice>)LanguageCombo.ItemsSource)
                .First(choice => choice.Value == _controller.Settings.Language);

            AutoSwitchToggle.IsOn = _controller.Settings.AutoEnabled;
            NotificationsToggle.IsOn = _controller.Settings.NotificationsEnabled;
            ScheduleTimelinePanel.Visibility = Visibility.Visible;

            var activePlan = _controller.Plans.FirstOrDefault(plan => plan.Id == _controller.ActivePlanId);
            var activeSlot = _controller.GetActiveSlot();
            var slotName = activeSlot is null ? _localizer.Get("Plan_Other") : GetSlotName(activeSlot.Value);
            var planName = activePlan?.Name ?? _localizer.Get("Plan_Unavailable");
            CurrentPlanText.Text = string.Format(_localizer.Get("CurrentPlan_Format"), slotName, planName);
            NextSwitchText.Text = _controller.CurrentDecision?.NextBoundary is { } boundary
                ? string.Format(_localizer.Get("NextSwitch_Format"), boundary.LocalDateTime)
                : _localizer.Get("NextSwitch_None");

            UpdateQuickButton(PowerSaverButton, SlotKind.PowerSaver, activeSlot);
            UpdateQuickButton(BalancedButton, SlotKind.Balanced, activeSlot);
            UpdateQuickButton(HighPerformanceButton, SlotKind.HighPerformance, activeSlot);
            UpdateQuickButton(UltimatePerformanceButton, SlotKind.UltimatePerformance, activeSlot);

            ScheduleList.ItemsSource = _controller.Settings.ScheduleRules
                .Where(rule => rule.Enabled)
                .OrderBy(rule => rule.Start)
                .Select(rule => new RuleDisplayItem(
                    rule.Id,
                    rule.Start.ToString("HH:mm"),
                    rule.End.ToString("HH:mm"),
                    GetSlotName(rule.Target)))
                .ToArray();
            RenderScheduleTimeline();

            DiagnosticsText.Text = string.Format(
                _localizer.Get("Diagnostics_Format"),
                _controller.Plans.Count,
                _controller.ActivePlanId?.ToString("D") ?? _localizer.Get("Plan_Unavailable"));
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void SetMappingItems(ComboBox comboBox, IReadOnlyList<PlanChoice> choices, SlotKind slot)
    {
        comboBox.ItemsSource = choices;
        _controller.Settings.SlotMappings.TryGetValue(slot, out var mappedId);
        comboBox.SelectedItem = choices.FirstOrDefault(choice => choice.Id == mappedId) ?? choices[0];
    }

    private void UpdateQuickButton(Button button, SlotKind slot, SlotKind? activeSlot)
    {
        var available = _controller.Settings.SlotMappings.TryGetValue(slot, out var planId)
            && planId is not null
            && _controller.Plans.Any(plan => plan.Id == planId.Value);
        button.IsEnabled = true;
        button.Opacity = activeSlot == slot ? 1 : available ? 0.74 : 0.55;
        button.BorderBrush = new SolidColorBrush(GetSlotColor(slot));
        button.BorderThickness = activeSlot == slot ? new Thickness(3) : new Thickness(1.5);
        button.FontWeight = activeSlot == slot ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    }

    private void ScheduleTimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs args) => RenderScheduleTimeline();

    private void RenderScheduleTimeline()
    {
        if (ScheduleTimelineCanvas is null
            || ScheduleTimelineCanvas.ActualWidth <= 0)
        {
            return;
        }

        ScheduleTimelineCanvas.Children.Clear();
        AddTimelineSegment(0, 24 * 60, _controller.Settings.DefaultSlot, true);
        foreach (var rule in _controller.Settings.ScheduleRules.Where(rule => rule.Enabled))
        {
            var start = (int)rule.Start.ToTimeSpan().TotalMinutes;
            var end = (int)rule.End.ToTimeSpan().TotalMinutes;
            if (start < end)
            {
                AddTimelineSegment(start, end, rule.Target, false);
            }
            else
            {
                AddTimelineSegment(start, 24 * 60, rule.Target, false);
                AddTimelineSegment(0, end, rule.Target, false);
            }
        }
    }

    private void AddTimelineSegment(int startMinute, int endMinute, SlotKind slot, bool isDefault)
    {
        if (endMinute <= startMinute)
        {
            return;
        }

        const double minutesPerDay = 24 * 60;
        var width = ScheduleTimelineCanvas.ActualWidth;
        var segment = new Border
        {
            Width = Math.Max(1, (endMinute - startMinute) / minutesPerDay * width),
            Height = 38,
            Background = new SolidColorBrush(GetSlotColor(slot)),
            Opacity = isDefault ? 0.48 : 0.96,
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 255, 255, 255)),
            BorderThickness = new Thickness(0.5, 0, 0.5, 0),
        };
        Canvas.SetLeft(segment, startMinute / minutesPerDay * width);
        ToolTipService.SetToolTip(
            segment,
            string.Format(
                _localizer.Get(isDefault ? "Timeline_Default_Format" : "Timeline_Rule_Format"),
                TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(startMinute)).ToString("HH:mm"),
                endMinute == 24 * 60 ? "24:00" : TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(endMinute)).ToString("HH:mm"),
                GetSlotName(slot)));
        ScheduleTimelineCanvas.Children.Add(segment);
    }

    private static Windows.UI.Color GetSlotColor(SlotKind slot) => slot switch
    {
        SlotKind.PowerSaver => Windows.UI.Color.FromArgb(255, 45, 164, 78),
        SlotKind.Balanced => Windows.UI.Color.FromArgb(255, 37, 131, 224),
        SlotKind.HighPerformance => Windows.UI.Color.FromArgb(255, 240, 140, 46),
        SlotKind.UltimatePerformance => Windows.UI.Color.FromArgb(255, 155, 89, 182),
        _ => Windows.UI.Color.FromArgb(255, 107, 114, 128),
    };

    private async void QuickSlotButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string value } || !Enum.TryParse<SlotKind>(value, out var slot))
        {
            return;
        }

        await SelectOrCreateSlotAsync(slot);
    }

    private async void AutoSwitchToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_updatingUi)
        {
            return;
        }

        try
        {
            await _controller.SetAutoEnabledAsync(AutoSwitchToggle.IsOn);
        }
        catch
        {
            RefreshUi();
        }
    }

    private async void SaveMappingsButton_Click(object sender, RoutedEventArgs args)
    {
        var mappings = new Dictionary<SlotKind, Guid?>
        {
            [SlotKind.PowerSaver] = (PowerSaverMapping.SelectedItem as PlanChoice)?.Id,
            [SlotKind.Balanced] = (BalancedMapping.SelectedItem as PlanChoice)?.Id,
            [SlotKind.HighPerformance] = (HighPerformanceMapping.SelectedItem as PlanChoice)?.Id,
            [SlotKind.UltimatePerformance] = (UltimatePerformanceMapping.SelectedItem as PlanChoice)?.Id,
        };
        var defaultSlot = (DefaultSlotCombo.SelectedItem as SlotChoice)?.Kind ?? SlotKind.Balanced;

        try
        {
            await _controller.SaveMappingsAsync(mappings, defaultSlot);
            ShowStatus(_localizer.Get("Status_Saved"), InfoBarSeverity.Success);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async void CreateMissingButton_Click(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _localizer.Get("Create_Title"),
            Content = _localizer.Get("Create_Text"),
            PrimaryButtonText = _localizer.Get("Create_Confirm"),
            CloseButtonText = _localizer.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var failures = await _controller.CreateAndBindMissingPlansAsync();
        ShowStatus(
            _localizer.Get(failures.Count == 0 ? "Status_Created" : "Status_CreatePartial"),
            failures.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async void AddRuleButton_Click(object sender, RoutedEventArgs args)
    {
        var target = (RuleTargetCombo.SelectedItem as SlotChoice)?.Kind ?? SlotKind.Balanced;
        var start = (StartTimeCombo.SelectedItem as TimeChoice)?.Value ?? new TimeOnly(12, 0);
        var end = (EndTimeCombo.SelectedItem as TimeChoice)?.Value ?? new TimeOnly(14, 0);
        var rule = new ScheduleRule(
            Guid.NewGuid(),
            start,
            end,
            target);

        var engine = new ScheduleEngine();
        var conflictingRule = _controller.Settings.ScheduleRules
            .Where(existing => existing.Enabled)
            .FirstOrDefault(existing => engine.Validate([existing, rule])
                .Any(error => error.Code == "Overlap" && error.RuleId == rule.Id));
        if (conflictingRule is not null)
        {
            ShowStatus(
                string.Format(
                    _localizer.Get("Error_Overlap_Format"),
                    start.ToString("HH:mm"),
                    end.ToString("HH:mm"),
                    conflictingRule.Start.ToString("HH:mm"),
                    conflictingRule.End.ToString("HH:mm")),
                InfoBarSeverity.Error);
            return;
        }

        var rules = _controller.Settings.ScheduleRules.Append(rule).ToArray();
        if (engine.Validate(rules).Count > 0)
        {
            ShowStatus(_localizer.Get("Error_Overlap"), InfoBarSeverity.Error);
            return;
        }

        await _controller.SaveRulesAsync(rules);
        ShowStatus(_localizer.Get("Status_Saved"), InfoBarSeverity.Success);
    }

    private async void DeleteRuleButton_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: Guid ruleId })
        {
            return;
        }

        await _controller.SaveRulesAsync(_controller.Settings.ScheduleRules.Where(rule => rule.Id != ruleId));
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_updatingUi)
        {
            return;
        }

        var state = await _startupService.SetEnabledAsync(StartupToggle.IsOn);
        _controller.Settings.StartAtLogin = state == StartupState.Enabled;
        await _controller.SaveGeneralSettingsAsync();
        if (StartupToggle.IsOn && state != StartupState.Enabled)
        {
            ShowStatus(_localizer.Get("Status_StartupBlocked"), InfoBarSeverity.Warning);
        }

        await RefreshStartupStateAsync();
    }

    private async void NotificationsToggle_Toggled(object sender, RoutedEventArgs args)
    {
        if (_updatingUi)
        {
            return;
        }

        _controller.Settings.NotificationsEnabled = NotificationsToggle.IsOn;
        await _controller.SaveGeneralSettingsAsync();
    }

    private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_updatingUi || LanguageCombo.SelectedItem is not LanguageChoice choice)
        {
            return;
        }

        _controller.Settings.Language = choice.Value;
        try
        {
            ApplicationLanguages.PrimaryLanguageOverride = choice.Value switch
            {
                LanguagePreference.English => "en-US",
                LanguagePreference.SimplifiedChinese => "zh-CN",
                _ => string.Empty,
            };
        }
        catch (InvalidOperationException)
        {
            // The selection is persisted and takes effect in packaged builds; unpackaged builds keep the Windows language.
        }
        await _controller.SaveGeneralSettingsAsync();

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _localizer.Get("Restart_Title"),
            Content = _localizer.Get("Restart_Text"),
            CloseButtonText = _localizer.Get("Common_OK"),
        };
        await dialog.ShowAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            await _controller.RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task RefreshStartupStateAsync()
    {
        var state = await _startupService.GetStateAsync();
        _updatingUi = true;
        StartupToggle.IsOn = state == StartupState.Enabled;
        StartupToggle.IsEnabled = state is not (StartupState.DisabledByPolicy or StartupState.Unsupported);
        _updatingUi = false;
    }

    private async Task ShowFirstRunDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _localizer.Get("FirstRun_Title"),
            Content = _localizer.Get("FirstRun_Text"),
            PrimaryButtonText = _localizer.Get("FirstRun_Enable"),
            CloseButtonText = _localizer.Get("FirstRun_Skip"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var state = await _startupService.SetEnabledAsync(true);
            _controller.Settings.StartAtLogin = state == StartupState.Enabled;
            if (state != StartupState.Enabled)
            {
                ShowStatus(_localizer.Get("Status_StartupBlocked"), InfoBarSeverity.Warning);
            }
        }

        _controller.Settings.FirstRunCompleted = true;
        await _controller.SaveGeneralSettingsAsync();
        await RefreshStartupStateAsync();
    }

    private IReadOnlyList<SlotChoice> GetSlotChoices() =>
    [
        new(SlotKind.PowerSaver, GetSlotName(SlotKind.PowerSaver)),
        new(SlotKind.Balanced, GetSlotName(SlotKind.Balanced)),
        new(SlotKind.HighPerformance, GetSlotName(SlotKind.HighPerformance)),
        new(SlotKind.UltimatePerformance, GetSlotName(SlotKind.UltimatePerformance)),
    ];

    private string GetSlotName(SlotKind slot) => _localizer.Get(slot switch
    {
        SlotKind.PowerSaver => "Slot_PowerSaver",
        SlotKind.Balanced => "Slot_Balanced",
        SlotKind.HighPerformance => "Slot_HighPerformance",
        SlotKind.UltimatePerformance => "Slot_UltimatePerformance",
        _ => "Plan_Other",
    });

    private void ShowError(Exception exception)
    {
        StartupDiagnostics.Write("UI operation failed", exception);
        var key = exception is PowerPlanException powerError
            ? powerError.Kind switch
            {
                PowerPlanErrorKind.PlanNotFound => "Error_PlanNotFound",
                PowerPlanErrorKind.AccessDenied => "Error_AccessDenied",
                PowerPlanErrorKind.Unsupported => "Error_Unsupported",
                PowerPlanErrorKind.PolicyRestricted => "Error_PolicyRestricted",
                _ => "Error_Generic",
            }
            : "Error_Generic";
        ShowStatus(_localizer.Get(key), InfoBarSeverity.Error);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private sealed record PlanChoice(Guid? Id, string Name);
    private sealed record SlotChoice(SlotKind Kind, string Name);
    private sealed record TimeChoice(TimeOnly Value, string Name);
    private sealed record LanguageChoice(LanguagePreference Value, string Name);
    private sealed record RuleDisplayItem(Guid Id, string StartText, string EndText, string TargetName);
}
