using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AutoPilotInput;

/// <summary>Registers global gestures while preserving the previous set if a change fails.</summary>
public sealed class HotkeyService : IDisposable
{
    public const int StartId = 1;
    public const int StopId = 2;
    public const int CaptureId = 3;
    public const int FinishCaptureId = 4;
    private const int WmHotkey = 0x0312;
    private const uint NoRepeat = 0x4000;
    private readonly HwndSource _source;
    private Dictionary<int, Registration> _registrations = [];
    private bool _disposed;

    private readonly record struct Gesture(uint Modifiers, uint VirtualKey);
    private sealed record Registration(int NativeId, int LogicalId, Gesture Gesture);

    public event Action<int>? Pressed;
    public IReadOnlyCollection<int> ReservedVirtualKeys => _registrations.Values.Select(x => (int)x.Gesture.VirtualKey).Distinct().ToArray();

    public HotkeyService(HwndSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Dispatcher.VerifyAccess();
        _source = source;
        _source.AddHook(WndProc);
    }

    public bool Configure(string start, string stop, string capture, out string? error)
    {
        _source.Dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var texts = new[] { start, stop, capture, "F7" };
        var desired = new List<Gesture>(4);
        foreach (var value in texts)
        {
            if (!TryParse(value, out var modifiers, out var key, out error)) return false;
            var gesture = new Gesture(modifiers, key);
            if (desired.Contains(gesture))
            {
                error = "快捷键不能相同，F7 已保留用于结束采集。 / Hotkeys must be different; F7 is reserved to finish capture.";
                return false;
            }
            desired.Add(gesture);
        }

        var candidate = new Dictionary<int, Registration>();
        var staged = new List<int>();
        for (var i = 0; i < desired.Count; i++)
        {
            var gesture = desired[i];
            var previous = _registrations.Values.FirstOrDefault(x => x.Gesture == gesture);
            if (previous is not null)
            {
                candidate.Add(previous.NativeId, previous with { LogicalId = i + 1 });
                continue;
            }

            var nativeId = Enumerable.Range(0x4A00, 256).First(id => !_registrations.ContainsKey(id) && !candidate.ContainsKey(id));
            if (!RegisterHotKey(_source.Handle, nativeId, gesture.Modifiers | NoRepeat, gesture.VirtualKey))
            {
                var nativeError = Marshal.GetLastWin32Error();
                foreach (var stagedId in staged) UnregisterHotKey(_source.Handle, stagedId);
                error = $"无法注册快捷键 {texts[i]}，可能已被其他程序占用。原快捷键保持有效。 / Could not register hotkey; previous hotkeys are unchanged. ({new Win32Exception(nativeError).Message})";
                return false;
            }
            staged.Add(nativeId);
            candidate.Add(nativeId, new Registration(nativeId, i + 1, gesture));
        }

        foreach (var obsolete in _registrations.Keys.Where(id => !candidate.ContainsKey(id)))
            UnregisterHotKey(_source.Handle, obsolete);
        _registrations = candidate;
        error = null;
        return true;
    }

    /// <summary>Accepts WPF key names and Ctrl/Alt/Shift/Win modifier prefixes, for example Ctrl+Alt+F8.</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey, out string? error)
    {
        modifiers = 0;
        virtualKey = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
        {
            error = "快捷键不能为空。 / A hotkey is required.";
            return false;
        }

        Key? mainKey = null;
        foreach (var token in text.Split('+', StringSplitOptions.TrimEntries))
        {
            uint modifier = token.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 2,
                "ALT" => 1,
                "SHIFT" => 4,
                "WIN" or "WINDOWS" => 8,
                _ => 0
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0)
                {
                    error = "快捷键不能重复修饰键。 / Duplicate hotkey modifier.";
                    return false;
                }
                modifiers |= modifier;
                continue;
            }
            var keyName = token.Length == 1 && char.IsAsciiDigit(token[0]) ? "D" + token : token;
            keyName = keyName.ToUpperInvariant() switch
            {
                "ESC" => "Escape",
                "PGUP" => "PageUp",
                "PGDN" => "PageDown",
                "DEL" => "Delete",
                "INS" => "Insert",
                _ => keyName
            };
            if (mainKey is not null || keyName.Length == 0 || char.IsAsciiDigit(keyName[0]) || !Enum.TryParse<Key>(keyName, true, out var parsed) || !Enum.IsDefined(parsed)
                || parsed is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed
                    or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                error = "快捷键需要一个主键，可搭配 Ctrl、Alt、Shift 或 Win。 / Use one key with optional Ctrl, Alt, Shift or Win.";
                return false;
            }
            mainKey = parsed;
        }
        if (mainKey is null || (virtualKey = (uint)KeyInterop.VirtualKeyFromKey(mainKey.Value)) is 0 or > 254)
        {
            error = "快捷键缺少有效主键。 / A valid main key is required.";
            return false;
        }
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && _registrations.TryGetValue(wParam.ToInt32(), out var registration))
        {
            handled = true;
            Pressed?.Invoke(registration.LogicalId);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _source.Dispatcher.VerifyAccess();
        _disposed = true;
        foreach (var id in _registrations.Keys) UnregisterHotKey(_source.Handle, id);
        _registrations.Clear();
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
