// SPDX-FileCopyrightText: 2026 Thermalyn Project
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Thermalyn.Services;

internal sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = 0x8001;

    // shellapi.h
    private const int Add = 0x00000000;
    private const int Delete = 0x00000002;
    private const int Modify = 0x00000001;
    private const int SetVersion = 0x00000004;
    private const int NotifyIconVersion4 = 4;

    private const uint HasCallbackMessage = 0x00000001;
    private const uint HasIcon = 0x00000002;
    private const uint HasTip = 0x00000004;
    private const uint UseStandardTooltip = 0x00000080;
    private const uint HasBalloon = 0x00000010;
    private const uint DiscardStaleBalloon = 0x00000040;

    private const uint BalloonWarningIcon = 0x00000002;
    private const uint BalloonSilent = 0x00000010;
    private const uint BalloonRespectQuietTime = 0x00000080;

    private const int LeftButtonUp = 0x0202;
    private const int LeftButtonDoubleClick = 0x0203;
    private const int Select = 0x0400;
    private const int KeySelect = 0x0401;
    private const int BalloonUserClick = 0x0405;
    private const int ContextMenu = 0x007B;

    // winuser.h
    private const uint MenuSeparator = 0x00000800;
    private const uint MenuItem = 0x00000000;
    private const uint ReturnCommand = 0x00000100;
    private const uint RightButtonMenu = 0x00000002;
    private const uint IconResourceVersion = 0x00030000;
    private const int LowWordMask = 0xFFFF;

    private const uint OpenCommand = 1;
    private const uint QuitCommand = 2;

    private const int TipLimit = 127;
    private const int TitleLimit = 63;
    private const int MessageLimit = 255;
    private readonly HwndSource _source;
    private readonly Action _restore;
    private readonly Action _quit;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyIconData _data;
    private bool _visible;

    public TrayService(Window window, Action restore, Action quit)
    {
        _restore = restore;
        _quit = quit;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            Window = _source.Handle,
            Id = 1,
            Callback = CallbackMessage,
            Icon = LoadApplicationIcon(),
            Tip = "Thermalyn",
            Info = "",
            Title = ""
        };
        _source.AddHook(WindowMessage);
    }

    public bool Update(string tooltip)
    {
        _data.Tip = Truncate(tooltip, TipLimit);
        _data.Flags = HasCallbackMessage | HasIcon | HasTip | UseStandardTooltip;
        if (_visible && Shell_NotifyIcon(Modify, ref _data)) return true;
        _visible = _data.Icon != IntPtr.Zero && Shell_NotifyIcon(Add, ref _data);
        if (_visible)
        {
            _data.Version = NotifyIconVersion4;
            Shell_NotifyIcon(SetVersion, ref _data);
        }
        return _visible;
    }

    public void Notify(string title, string message)
    {
        if (!_visible) return;
        _data.Flags = HasBalloon | DiscardStaleBalloon;
        _data.Title = Truncate(title, TitleLimit);
        _data.Info = Truncate(message, MessageLimit);
        _data.InfoFlags = BalloonWarningIcon | BalloonSilent | BalloonRespectQuietTime;
        Shell_NotifyIcon(Modify, ref _data);
        _data.Info = "";
    }

    private static string Truncate(string value, int limit) => value.Length > limit ? value[..limit] : value;

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && (uint)message == _taskbarCreated)
        {
            _visible = false;
            if (!Update(_data.Tip)) _restore();
        }
        if (message != CallbackMessage) return IntPtr.Zero;
        var notification = (int)(lParam.ToInt64() & LowWordMask);
        if (notification is Select or KeySelect or BalloonUserClick or LeftButtonUp or LeftButtonDoubleClick) _restore();
        else if (notification == ContextMenu) ShowMenu();
        handled = true;
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) return;
        try
        {
            AppendMenu(menu, MenuItem, OpenCommand, LocalizationService.Get("Tray.Open"));
            AppendMenu(menu, MenuSeparator, 0, "");
            AppendMenu(menu, MenuItem, QuitCommand, LocalizationService.Get("Tray.Quit"));
            GetCursorPos(out var point);
            // The owner must be foreground and needs a posted message after, or the menu sticks.
            SetForegroundWindow(_source.Handle);
            var command = TrackPopupMenuEx(menu, ReturnCommand | RightButtonMenu, point.X, point.Y, _source.Handle, IntPtr.Zero);
            PostMessage(_source.Handle, 0, IntPtr.Zero, IntPtr.Zero);
            if (command == OpenCommand) _restore();
            else if (command == QuitCommand) _quit();
        }
        finally { DestroyMenu(menu); }
    }

    private static IntPtr LoadApplicationIcon()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Thermalyn;component/Assets/Thermalyn.ico")).Stream;
        using var reader = new BinaryReader(stream);
        reader.ReadUInt16(); reader.ReadUInt16();
        var count = reader.ReadUInt16();
        uint length = 0, offset = 0;
        for (var i = 0; i < count; i++)
        {
            // ICONDIRENTRY: width, height, colours, reserved, planes, bit count.
            var width = reader.ReadByte();
            reader.ReadBytes(7);
            var size = reader.ReadUInt32(); var start = reader.ReadUInt32();
            if (i == 0 || width == 32) { length = size; offset = start; }
            if (width == 32) break;
        }
        stream.Position = offset;
        var bytes = reader.ReadBytes((int)length);
        return CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, IconResourceVersion, 32, 32, 0);
    }

    public void Dispose()
    {
        if (_visible) Shell_NotifyIcon(Delete, ref _data);
        _visible = false;
        _source.RemoveHook(WindowMessage);
        if (_data.Icon != IntPtr.Zero) { DestroyIcon(_data.Icon); _data.Icon = IntPtr.Zero; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
