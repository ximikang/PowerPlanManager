using System.Runtime.InteropServices;
using PowerManager.App.Native;
using PowerManager.Core.Models;

namespace PowerManager.App.Services;

public sealed class TrayService : ITrayService
{
    private const uint TrayIconId = 1;
    private const uint CallbackMessage = TrayNativeMethods.WmApp + 17;
    private const uint CommandPowerSaver = 101;
    private const uint CommandBalanced = 102;
    private const uint CommandHighPerformance = 103;
    private const uint CommandUltimatePerformance = 104;
    private const uint CommandAuto = 110;
    private const uint CommandOpen = 120;
    private const uint CommandExit = 121;

    private readonly IStringLocalizer _localizer;
    private readonly TrayNativeMethods.WindowProcedure _windowProcedure;
    private nint _windowHandle;
    private nint _previousWindowProcedure;
    private nint _defaultIconHandle;
    private readonly Dictionary<SlotKind, nint> _slotIconHandles = [];
    private uint _taskbarCreatedMessage;
    private TrayState _state = new(string.Empty, null, false, new HashSet<SlotKind>());
    private bool _disposed;

    public TrayService(IStringLocalizer localizer)
    {
        _localizer = localizer;
        _windowProcedure = WindowProcedure;
    }

    public event EventHandler<SlotKind>? SlotInvoked;
    public event EventHandler? FlyoutRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? AutoToggleRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SystemStateChanged;

    public void Initialize(nint windowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_windowHandle != 0)
        {
            return;
        }

        _windowHandle = windowHandle;
        _taskbarCreatedMessage = TrayNativeMethods.RegisterWindowMessage("TaskbarCreated");
        _previousWindowProcedure = TrayNativeMethods.SetWindowLongPtr(
            _windowHandle,
            TrayNativeMethods.GwlpWndProc,
            Marshal.GetFunctionPointerForDelegate(_windowProcedure));

        LoadIcons();
        AddIcon();
    }

    public void Update(TrayState state)
    {
        _state = state;
        if (_windowHandle == 0)
        {
            return;
        }

        var data = CreateIconData();
        TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NimModify, ref data);
    }

    public void ShowNotification(string title, string message)
    {
        if (_windowHandle == 0)
        {
            return;
        }

        var data = CreateIconData();
        data.Flags = TrayNativeMethods.NifInfo;
        data.InfoTitle = title;
        data.Info = message;
        data.InfoFlags = TrayNativeMethods.NiifInfo;
        TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_windowHandle != 0)
        {
            var data = CreateIconData();
            TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NimDelete, ref data);
            if (_previousWindowProcedure != 0)
            {
                TrayNativeMethods.SetWindowLongPtr(_windowHandle, TrayNativeMethods.GwlpWndProc, _previousWindowProcedure);
            }
        }

        foreach (var iconHandle in _slotIconHandles.Values.Append(_defaultIconHandle).Where(handle => handle != 0).Distinct())
        {
            TrayNativeMethods.DestroyIcon(iconHandle);
        }

        _slotIconHandles.Clear();
        _defaultIconHandle = 0;
    }

    private void AddIcon()
    {
        var data = CreateIconData();
        TrayNativeMethods.ShellNotifyIcon(TrayNativeMethods.NimAdd, ref data);
    }

    private TrayNativeMethods.NotifyIconData CreateIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconData>(),
        WindowHandle = _windowHandle,
        Id = TrayIconId,
        Flags = TrayNativeMethods.NifMessage | TrayNativeMethods.NifIcon | TrayNativeMethods.NifTip,
        CallbackMessage = CallbackMessage,
        IconHandle = GetCurrentIconHandle(),
        ToolTip = string.IsNullOrWhiteSpace(_state.ToolTip) ? _localizer.Get("AppDisplayName") : _state.ToolTip,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private void LoadIcons()
    {
        _defaultIconHandle = LoadIcon("PowerManager.Other.ico");
        if (_defaultIconHandle == 0)
        {
            _defaultIconHandle = LoadIcon("PowerManager.ico");
        }

        _slotIconHandles[SlotKind.PowerSaver] = LoadIcon("PowerManager.PowerSaver.ico");
        _slotIconHandles[SlotKind.Balanced] = LoadIcon("PowerManager.Balanced.ico");
        _slotIconHandles[SlotKind.HighPerformance] = LoadIcon("PowerManager.HighPerformance.ico");
        _slotIconHandles[SlotKind.UltimatePerformance] = LoadIcon("PowerManager.UltimatePerformance.ico");
    }

    private static nint LoadIcon(string fileName) => TrayNativeMethods.LoadImage(
        0,
        Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
        TrayNativeMethods.ImageIcon,
        20,
        20,
        TrayNativeMethods.LrLoadFromFile);

    private nint GetCurrentIconHandle()
    {
        if (_state.ActiveSlot is { } activeSlot
            && _slotIconHandles.TryGetValue(activeSlot, out var iconHandle)
            && iconHandle != 0)
        {
            return iconHandle;
        }

        return _defaultIconHandle;
    }

    private nint WindowProcedure(nint windowHandle, uint message, nuint wParam, nint lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            AddIcon();
            return 0;
        }

        if (message == CallbackMessage)
        {
            var mouseMessage = unchecked((uint)lParam.ToInt64());
            if (mouseMessage == TrayNativeMethods.WmLButtonUp)
            {
                FlyoutRequested?.Invoke(this, EventArgs.Empty);
                return 0;
            }

            if (mouseMessage is TrayNativeMethods.WmRButtonUp or TrayNativeMethods.WmContextMenu)
            {
                ShowContextMenu();
                return 0;
            }
        }

        if (message == TrayNativeMethods.WmTimeChange
            || (message == TrayNativeMethods.WmPowerBroadcast && wParam == TrayNativeMethods.PbtApmResumeAutomatic))
        {
            SystemStateChanged?.Invoke(this, EventArgs.Empty);
        }

        return TrayNativeMethods.CallWindowProc(_previousWindowProcedure, windowHandle, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        if (!TrayNativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        var menu = TrayNativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            AddSlotMenuItem(menu, CommandPowerSaver, SlotKind.PowerSaver, "Slot_PowerSaver");
            AddSlotMenuItem(menu, CommandBalanced, SlotKind.Balanced, "Slot_Balanced");
            AddSlotMenuItem(menu, CommandHighPerformance, SlotKind.HighPerformance, "Slot_HighPerformance");
            AddSlotMenuItem(menu, CommandUltimatePerformance, SlotKind.UltimatePerformance, "Slot_UltimatePerformance");
            TrayNativeMethods.AppendMenu(menu, TrayNativeMethods.MfSeparator, 0, null);
            TrayNativeMethods.AppendMenu(
                menu,
                TrayNativeMethods.MfString | (_state.AutoEnabled ? TrayNativeMethods.MfChecked : 0),
                CommandAuto,
                _localizer.Get("Tray_Auto"));
            TrayNativeMethods.AppendMenu(menu, TrayNativeMethods.MfString, CommandOpen, _localizer.Get("Tray_Open"));
            TrayNativeMethods.AppendMenu(menu, TrayNativeMethods.MfString, CommandExit, _localizer.Get("Tray_Exit"));

            TrayNativeMethods.SetForegroundWindow(_windowHandle);
            var command = TrayNativeMethods.TrackPopupMenu(
                menu,
                TrayNativeMethods.TpmRightButton | TrayNativeMethods.TpmReturnCmd,
                point.X,
                point.Y,
                0,
                _windowHandle,
                0);
            DispatchCommand(command);
        }
        finally
        {
            TrayNativeMethods.DestroyMenu(menu);
        }
    }

    private void AddSlotMenuItem(nint menu, uint command, SlotKind slot, string resourceKey)
    {
        var flags = TrayNativeMethods.MfString;
        if (_state.ActiveSlot == slot)
        {
            flags |= TrayNativeMethods.MfChecked;
        }

        var slotName = _localizer.Get(resourceKey);
        var label = _state.AvailableSlots.Contains(slot)
            ? slotName
            : string.Format(_localizer.Get("Tray_Create_Format"), slotName);
        TrayNativeMethods.AppendMenu(menu, flags, command, label);
    }

    private void DispatchCommand(uint command)
    {
        switch (command)
        {
            case CommandPowerSaver:
                SlotInvoked?.Invoke(this, SlotKind.PowerSaver);
                break;
            case CommandBalanced:
                SlotInvoked?.Invoke(this, SlotKind.Balanced);
                break;
            case CommandHighPerformance:
                SlotInvoked?.Invoke(this, SlotKind.HighPerformance);
                break;
            case CommandUltimatePerformance:
                SlotInvoked?.Invoke(this, SlotKind.UltimatePerformance);
                break;
            case CommandAuto:
                AutoToggleRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CommandOpen:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CommandExit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
