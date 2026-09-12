using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoPilotInput.Core;

/// <summary>Windows input uses physical desktop pixels; the app manifest is PerMonitorV2.</summary>
public static class NativeInput
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct HardwareInput { public uint Message; public ushort ParamL, ParamH; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    public static int GetInputSize() => Marshal.SizeOf<Input>();
    public static long GetForeground() => GetForegroundWindow().ToInt64();
    public static uint GetProcessId(long handle) { GetWindowThreadProcessId((IntPtr)handle, out var id); return id; }
    public static string GetWindowTitle(long handle)
    {
        var text = new StringBuilder(1024);
        GetWindowText((IntPtr)handle, text, text.Capacity);
        return text.ToString();
    }
    public static bool IsValidTarget(long handle, uint processId) => handle != 0 && processId != 0 &&
        IsWindow((IntPtr)handle) && IsWindowVisible((IntPtr)handle) && GetProcessId(handle) == processId;
    public static bool IsForeground(long handle) => handle != 0 && !IsIconic((IntPtr)handle) && GetForeground() == handle;
    public static ClickPoint? CapturePoint(long handle, CoordinateMode mode)
    {
        if (!GetCursorPos(out var point)) return null;
        if (mode == CoordinateMode.Window && (handle == 0 || !ScreenToClient((IntPtr)handle, ref point))) return null;
        return new ClickPoint { X = point.X, Y = point.Y };
    }

    public static string? ValidatePoint(long handle, CoordinateMode mode, ClickPoint point)
    {
        if (point is null) return "鼠标坐标不能为空。";
        var native = new Point { X = point.X, Y = point.Y };
        if (mode == CoordinateMode.Window)
        {
            if (!GetClientRect((IntPtr)handle, out var rect)) return "无法读取目标窗口范围。";
            if (native.X < rect.Left || native.Y < rect.Top || native.X >= rect.Right || native.Y >= rect.Bottom)
                return "鼠标坐标超出目标窗口内容区域。";
            if (!ClientToScreen((IntPtr)handle, ref native)) return "无法转换窗口坐标。";
        }
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77);
        if (native.X < left || native.Y < top || native.X >= (long)left + GetSystemMetrics(78) || native.Y >= (long)top + GetSystemMetrics(79))
            return "鼠标坐标超出当前桌面范围。";
        var hit = WindowFromPoint(native);
        if (hit == IntPtr.Zero || GetAncestor(hit, 2).ToInt64() != GetAncestor((IntPtr)handle, 2).ToInt64())
            return "鼠标坐标没有位于选定目标窗口内，可能被其他窗口遮挡。";
        return null;
    }

    /// <summary>Validate persistent action fields; live target binding is checked by the engine.</summary>
    public static List<string> ValidateAction(AutomationAction action, bool requireInput = true)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(action.Kind) || !Enum.IsDefined(action.IntervalMode) || !Enum.IsDefined(action.CoordinateMode) || !Enum.IsDefined(action.MouseButton))
            errors.Add("动作类型或模式无效。");
        if (string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 100) errors.Add("动作名称需包含 1–100 个字符。");
        static bool Seconds(double number) => double.IsFinite(number) && number >= .1 && number <= 604800;
        if (!Seconds(action.IntervalSeconds) || !Seconds(action.MinSeconds) || !Seconds(action.MaxSeconds)) errors.Add("时间间隔必须介于 0.1 秒和 7 天之间。");
        if (action.MaxSeconds < action.MinSeconds) errors.Add("最大间隔不能小于最小间隔。");
        if (!double.IsFinite(action.JitterSeconds) || action.JitterSeconds < 0 || action.JitterSeconds > 604800) errors.Add("随机幅度无效。");
        if (action.IntervalMode == IntervalMode.Jitter && (!Seconds(action.IntervalSeconds - action.JitterSeconds) || !Seconds(action.IntervalSeconds + action.JitterSeconds)))
            errors.Add("随机间隔的上下限必须介于 0.1 秒和 7 天之间。");
        if (action.Keys is null || action.Keys.Count > 8 || action.Keys.Any(key => key < 8 || key > 254) || action.Keys.Distinct().Count() != action.Keys.Count)
            errors.Add("按键组合无效（最多 8 个不重复按键）。");
        else if (requireInput && action.Kind == ActionKind.Keyboard && action.Keys.Count == 0) errors.Add("请先选择或录制按键。");
        if (action.Points is null || action.Points.Count > 100 || action.Points.Any(point => point is null || Math.Abs((long)point.X) > 1000000 || Math.Abs((long)point.Y) > 1000000))
            errors.Add("鼠标坐标列表无效（最多 100 个位置）。");
        else if (requireInput && action.Kind == ActionKind.Mouse && action.Points.Count == 0) errors.Add("请先录制至少一个鼠标位置。");
        if (action.PointDelayMs < 0 || action.PointDelayMs > 60000) errors.Add("位置间停顿必须介于 0 和 60000 毫秒之间。");
        return errors;
    }

    private static bool IsExtended(int key) => key is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F or 0x90 or 0xA3 or 0xA5;
    private static Input KeyInput(int key, bool up) => new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Vk = (ushort)key, Flags = (up ? 2u : 0u) | (IsExtended(key) ? 1u : 0u) } } };
    private static void SendChecked(Input input)
    {
        if (SendInput(1, [input], GetInputSize()) != 1)
            throw new AutomationInputException(InputErrorKind.Input, "Windows 未接受输入；目标程序可能以管理员权限运行。" + $" (Win32 {Marshal.GetLastWin32Error()})");
    }
    public static async Task SendKeyboardAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken)
    {
        var pressed = new List<int>();
        try
        {
            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SendChecked(KeyInput(key, false));
                pressed.Add(key);
            }
            await Task.Delay(30, cancellationToken);
        }
        finally
        {
            Exception? releaseError = null;
            foreach (var key in pressed.AsEnumerable().Reverse())
            {
                try { SendChecked(KeyInput(key, true)); }
                catch (Exception error) { releaseError ??= error; }
            }
            if (releaseError is not null) throw releaseError;
        }
    }
    public static async Task SendMouseAsync(long handle, CoordinateMode mode, ClickPoint point, MouseButton button, bool doubleClick, CancellationToken cancellationToken)
    {
        for (var click = 0; click < (doubleClick ? 2 : 1); click++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsForeground(handle)) throw new AutomationInputException(InputErrorKind.Window, "目标窗口已离开前台，动作已保护性中断。");
            var error = ValidatePoint(handle, mode, point);
            if (error is not null) throw new AutomationInputException(InputErrorKind.InvalidPoint, error);
            var position = new Point { X = point.X, Y = point.Y };
            if (mode == CoordinateMode.Window && !ClientToScreen((IntPtr)handle, ref position)) throw new AutomationInputException(InputErrorKind.InvalidPoint, "转换窗口坐标失败。");
            if (!SetCursorPos(position.X, position.Y)) throw new AutomationInputException(InputErrorKind.Input, "无法移动鼠标指针。");
            // Recheck after moving the pointer: hover can open overlays or change activation.
            if (!IsForeground(handle)) throw new AutomationInputException(InputErrorKind.Window, "目标窗口已离开前台。");
            error = ValidatePoint(handle, mode, point);
            if (error is not null) throw new AutomationInputException(InputErrorKind.InvalidPoint, error);
            var down = button == MouseButton.Left ? 0x2u : 0x8u;
            var up = button == MouseButton.Left ? 0x4u : 0x10u;
            SendChecked(new Input { Data = new InputUnion { Mouse = new MouseInput { Flags = down } } });
            try { await Task.Delay(20, cancellationToken); }
            finally { SendChecked(new Input { Data = new InputUnion { Mouse = new MouseInput { Flags = up } } }); }
            if (doubleClick && click == 0) await Task.Delay(55, cancellationToken);
        }
    }
}

public sealed class WindowsAutomationInput : IAutomationInput
{
    public bool IsValidTarget(long handle, uint processId) => NativeInput.IsValidTarget(handle, processId);
    public bool IsForeground(long handle) => NativeInput.IsForeground(handle);
    public string? ValidatePoint(long handle, CoordinateMode mode, ClickPoint point) => NativeInput.ValidatePoint(handle, mode, point);
    public Task SendKeyboardAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken) => NativeInput.SendKeyboardAsync(keys, cancellationToken);
    public Task SendMouseAsync(long handle, CoordinateMode mode, ClickPoint point, MouseButton button, bool doubleClick, CancellationToken cancellationToken) => NativeInput.SendMouseAsync(handle, mode, point, button, doubleClick, cancellationToken);
}
