using Microsoft.UI.Dispatching;
using PowerManager.Core.Models;
using PowerManager.Core.Services;

namespace PowerManager.App.Services;

public sealed class AppController : IDisposable
{
    private readonly IPowerPlanService _powerPlans;
    private readonly ISettingsStore _settingsStore;
    private readonly IScheduleEngine _scheduleEngine;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly Timer _timer;
    private bool _disposed;

    public AppController(
        IPowerPlanService powerPlans,
        ISettingsStore settingsStore,
        IScheduleEngine scheduleEngine,
        DispatcherQueue dispatcherQueue)
    {
        _powerPlans = powerPlans;
        _settingsStore = settingsStore;
        _scheduleEngine = scheduleEngine;
        _dispatcherQueue = dispatcherQueue;
        _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? StateChanged;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<SlotKind>? PlanChanged;

    public AppSettings Settings { get; private set; } = AppSettings.CreateDefault();
    public IReadOnlyList<PowerPlanInfo> Plans { get; private set; } = [];
    public Guid? ActivePlanId { get; private set; }
    public ScheduleDecision? CurrentDecision { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Settings = await _settingsStore.LoadAsync(cancellationToken);
        await RefreshAsync(cancellationToken);
        if (Settings.AutoEnabled)
        {
            await ApplyAutomaticAsync(cancellationToken);
        }
        else
        {
            RescheduleTimer();
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Plans = await _powerPlans.GetPlansAsync(cancellationToken);
        ActivePlanId = Plans.FirstOrDefault(plan => plan.IsActive)?.Id
            ?? await _powerPlans.GetActivePlanIdAsync(cancellationToken);
        RaiseStateChanged();
    }

    public async Task SwitchSlotAsync(SlotKind slot, bool manual = true, CancellationToken cancellationToken = default)
    {
        await ExecuteSerializedAsync(async () =>
        {
            if (!Settings.SlotMappings.TryGetValue(slot, out var planId) || planId is null)
            {
                throw new PowerPlanException(PowerPlanErrorKind.PlanNotFound, "resolve-slot", 1168);
            }

            var plans = await _powerPlans.GetPlansAsync(cancellationToken);
            if (plans.All(plan => plan.Id != planId.Value))
            {
                throw new PowerPlanException(PowerPlanErrorKind.PlanNotFound, "resolve-slot", 1168);
            }

            if (manual && Settings.AutoEnabled)
            {
                _scheduleEngine.SetManualOverride(slot, DateTimeOffset.Now, Settings);
            }

            var previousActiveId = Plans.FirstOrDefault(plan => plan.IsActive)?.Id;
            await _powerPlans.SetActivePlanAsync(planId.Value, cancellationToken);
            Plans = await _powerPlans.GetPlansAsync(cancellationToken);
            ActivePlanId = planId.Value;
            CurrentDecision = _scheduleEngine.Evaluate(DateTimeOffset.Now, Settings);
            RescheduleTimer();
            RaiseStateChanged();
            if (previousActiveId != planId.Value)
            {
                PlanChanged?.Invoke(this, slot);
            }
        });
    }

    public async Task SetAutoEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        Settings.AutoEnabled = enabled;
        if (!enabled)
        {
            _scheduleEngine.ClearManualOverride();
            CurrentDecision = null;
        }

        await _settingsStore.SaveAsync(Settings, cancellationToken);
        if (enabled)
        {
            await ApplyAutomaticAsync(cancellationToken);
        }
        else
        {
            RescheduleTimer();
            RaiseStateChanged();
        }
    }

    public async Task ApplyAutomaticAsync(CancellationToken cancellationToken = default)
    {
        if (!Settings.AutoEnabled)
        {
            RescheduleTimer();
            return;
        }

        await ExecuteSerializedAsync(async () =>
        {
            var decision = _scheduleEngine.Evaluate(DateTimeOffset.Now, Settings);
            CurrentDecision = decision;
            if (!Settings.SlotMappings.TryGetValue(decision.Target, out var planId) || planId is null)
            {
                throw new PowerPlanException(PowerPlanErrorKind.PlanNotFound, "automatic-slot", 1168);
            }

            var plans = await _powerPlans.GetPlansAsync(cancellationToken);
            if (plans.All(plan => plan.Id != planId.Value))
            {
                throw new PowerPlanException(PowerPlanErrorKind.PlanNotFound, "automatic-slot", 1168);
            }

            var activeId = plans.FirstOrDefault(plan => plan.IsActive)?.Id;
            var changed = activeId != planId.Value;
            if (changed)
            {
                await _powerPlans.SetActivePlanAsync(planId.Value, cancellationToken);
                plans = await _powerPlans.GetPlansAsync(cancellationToken);
            }

            Plans = plans;
            ActivePlanId = planId.Value;
            RescheduleTimer();
            RaiseStateChanged();
            if (changed)
            {
                PlanChanged?.Invoke(this, decision.Target);
            }
        });
    }

    public async Task SaveMappingsAsync(
        IReadOnlyDictionary<SlotKind, Guid?> mappings,
        SlotKind defaultSlot,
        CancellationToken cancellationToken = default)
    {
        foreach (var slot in Enum.GetValues<SlotKind>())
        {
            Settings.SlotMappings[slot] = mappings.GetValueOrDefault(slot);
        }

        Settings.DefaultSlot = defaultSlot;
        await _settingsStore.SaveAsync(Settings, cancellationToken);
        if (Settings.AutoEnabled)
        {
            await ApplyAutomaticAsync(cancellationToken);
        }
        else
        {
            RaiseStateChanged();
        }
    }

    public async Task SaveRulesAsync(IEnumerable<ScheduleRule> rules, CancellationToken cancellationToken = default)
    {
        var materialized = rules.ToList();
        var errors = _scheduleEngine.Validate(materialized);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(",", errors.Select(error => error.Code)));
        }

        Settings.ScheduleRules = materialized;
        _scheduleEngine.ClearManualOverride();
        await _settingsStore.SaveAsync(Settings, cancellationToken);
        if (Settings.AutoEnabled)
        {
            await ApplyAutomaticAsync(cancellationToken);
        }
        else
        {
            RaiseStateChanged();
        }
    }

    public async Task<IReadOnlyDictionary<SlotKind, Exception>> CreateAndBindMissingPlansAsync(
        CancellationToken cancellationToken = default)
    {
        var failures = new Dictionary<SlotKind, Exception>();
        var plans = await _powerPlans.GetPlansAsync(cancellationToken);
        foreach (var slot in Enum.GetValues<SlotKind>())
        {
            var canonicalId = StandardPowerPlans.BySlot[slot];
            var existing = plans.FirstOrDefault(plan => plan.Id == canonicalId);
            if (existing is not null)
            {
                Settings.SlotMappings[slot] = existing.Id;
                continue;
            }

            if (Settings.SlotMappings.TryGetValue(slot, out var mappedId)
                && mappedId is not null
                && plans.Any(plan => plan.Id == mappedId.Value))
            {
                continue;
            }

            try
            {
                Settings.SlotMappings[slot] = await _powerPlans.DuplicateStandardPlanAsync(slot, cancellationToken);
                plans = await _powerPlans.GetPlansAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                failures[slot] = exception;
            }
        }

        await _settingsStore.SaveAsync(Settings, cancellationToken);
        await RefreshAsync(cancellationToken);
        return failures;
    }

    public async Task<Guid> CreateAndBindPlanAsync(
        SlotKind slot,
        CancellationToken cancellationToken = default)
    {
        Guid result = Guid.Empty;
        await ExecuteSerializedAsync(async () =>
        {
            var plans = await _powerPlans.GetPlansAsync(cancellationToken);
            if (Settings.SlotMappings.TryGetValue(slot, out var mappedId)
                && mappedId is not null
                && plans.Any(plan => plan.Id == mappedId.Value))
            {
                result = mappedId.Value;
                return;
            }

            var canonicalId = StandardPowerPlans.BySlot[slot];
            var canonicalPlan = plans.FirstOrDefault(plan => plan.Id == canonicalId);
            result = canonicalPlan?.Id
                ?? await _powerPlans.DuplicateStandardPlanAsync(slot, cancellationToken);

            Settings.SlotMappings[slot] = result;
            await _settingsStore.SaveAsync(Settings, cancellationToken);
            Plans = await _powerPlans.GetPlansAsync(cancellationToken);
            ActivePlanId = Plans.FirstOrDefault(plan => plan.IsActive)?.Id
                ?? await _powerPlans.GetActivePlanIdAsync(cancellationToken);
            RaiseStateChanged();
        });

        return result;
    }

    public async Task SaveGeneralSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _settingsStore.SaveAsync(Settings, cancellationToken);
        RaiseStateChanged();
    }

    public SlotKind? GetActiveSlot()
    {
        if (ActivePlanId is null)
        {
            return null;
        }

        foreach (var (slot, planId) in Settings.SlotMappings)
        {
            if (planId == ActivePlanId)
            {
                return slot;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _operationLock.Dispose();
    }

    private async Task ExecuteSerializedAsync(Func<Task> operation)
    {
        await _operationLock.WaitAsync();
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            ErrorOccurred?.Invoke(this, exception);
            RescheduleTimer();
            throw;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private void RescheduleTimer()
    {
        if (!Settings.AutoEnabled)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        CurrentDecision = _scheduleEngine.Evaluate(DateTimeOffset.Now, Settings);
        if (CurrentDecision.NextBoundary is null)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        var delay = CurrentDecision.NextBoundary.Value - DateTimeOffset.Now;
        if (delay < TimeSpan.FromMilliseconds(50))
        {
            delay = TimeSpan.FromMilliseconds(50);
        }

        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void OnTimer(object? state)
    {
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await ApplyAutomaticAsync();
            }
            catch
            {
                // ErrorOccurred already carries the actionable failure to the UI.
            }
        });
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
