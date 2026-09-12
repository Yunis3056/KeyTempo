using System.Text.Json.Serialization;

namespace AutoPilotInput.Core;

public enum ActionKind { Keyboard, Mouse }
public enum IntervalMode { Fixed, Range, Jitter }
public enum CoordinateMode { Screen, Window }
public enum MouseButton { Left, Right }
public enum ErrorPolicy { PauseAction, SkipCycle, PauseAll }
public enum InputErrorKind { Window, Input, InvalidPoint }

public sealed class ClickPoint
{
    public int X { get; set; }
    public int Y { get; set; }
    public override string ToString() => $"({X}, {Y})";
}

public sealed class AutomationAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "新动作";
    public bool Enabled { get; set; } = true;
    public ActionKind Kind { get; set; }
    public List<int> Keys { get; set; } = [];
    public IntervalMode IntervalMode { get; set; }
    public double IntervalSeconds { get; set; } = 300;
    public double MinSeconds { get; set; } = 270;
    public double MaxSeconds { get; set; } = 330;
    public double JitterSeconds { get; set; } = 30;
    public bool RunImmediately { get; set; }
    public CoordinateMode CoordinateMode { get; set; }
    public List<ClickPoint> Points { get; set; } = [];
    public MouseButton MouseButton { get; set; }
    public bool DoubleClick { get; set; }
    public int PointDelayMs { get; set; } = 150;
    [JsonIgnore] public long TargetHandle { get; set; }
    [JsonIgnore] public uint TargetProcessId { get; set; }
    public string TargetTitle { get; set; } = "";
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string Accent { get; set; } = "#9D7BFF";
    public string Animation { get; set; } = "Standard";
    public string Language { get; set; } = "zh-CN";
    public double Opacity { get; set; } = 1;
    public bool Sound { get; set; }
    public bool Notifications { get; set; } = true;
    public string StartHotkey { get; set; } = "F8";
    public string StopHotkey { get; set; } = "F9";
    public string CaptureHotkey { get; set; } = "F6";
    public string Repository { get; set; } = "";
    public ErrorPolicy WindowFailure { get; set; } = ErrorPolicy.PauseAction;
    public ErrorPolicy InputFailure { get; set; } = ErrorPolicy.PauseAction;
    public ErrorPolicy InvalidPoint { get; set; } = ErrorPolicy.PauseAction;
}

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<AutomationAction> Actions { get; set; } = [new()];
    public AppSettings Settings { get; set; } = new();
}

public sealed class ActionStatus
{
    public string State { get; init; } = "Stopped";
    public double RemainingSeconds { get; init; }
    public int Count { get; init; }
    public DateTimeOffset? LastRun { get; init; }
    public DateTimeOffset? NextRun { get; init; }
    public string Error { get; init; } = "";
}

public sealed class AutomationInputException(InputErrorKind kind, string message) : Exception(message)
{
    public InputErrorKind Kind { get; } = kind;
}

public interface IAutomationInput
{
    bool IsValidTarget(long handle, uint processId);
    bool IsForeground(long handle);
    string? ValidatePoint(long handle, CoordinateMode mode, ClickPoint point);
    Task SendKeyboardAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken);
    Task SendMouseAsync(long handle, CoordinateMode mode, ClickPoint point, MouseButton button, bool doubleClick, CancellationToken cancellationToken);
}
