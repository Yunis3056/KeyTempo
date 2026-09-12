using AutoPilotInput.Core;

var tests = new (string Name, Func<Task> Test)[]
{
    ("No default key; invalid intervals and missing target cannot run", async () =>
    {
        var (engine, action, _, _) = Harness();
        action.Keys.Clear(); Check(engine.Start().Count > 0, "empty keys accepted");
        action.Keys.Add(65); action.IntervalSeconds = double.NaN; Check(engine.Start().Count > 0, "NaN accepted");
        action.IntervalSeconds = 300; action.TargetHandle = 0; Check(engine.Start().Count > 0, "missing target accepted");
        Check(new AppConfig().Actions[0].Keys.Count == 0, "default key exists");
        await Task.CompletedTask;
    }),
    ("Five minute wait and no catch-up burst", async () =>
    {
        var (engine, _, input, clock) = Harness(); Start(engine);
        clock.Advance(299); await engine.TickAsync(); Check(input.KeyboardCalls == 0, "early execution");
        clock.Advance(1); await engine.TickAsync(); Check(input.KeyboardCalls == 1, "missing execution");
        clock.Advance(3600); await engine.TickAsync(); await engine.TickAsync(); Check(input.KeyboardCalls == 2, "catch-up burst");
    }),
    ("Immediate start and independent action intervals", async () =>
    {
        var first = Action(); first.RunImmediately = true; first.IntervalSeconds = 10;
        var second = Action(); second.IntervalSeconds = 30;
        var clock = new ManualClock(); var input = new FakeInput();
        using var engine = new AutomationEngine(new List<AutomationAction> { first, second }, new(), input, clock);
        Start(engine); await engine.TickAsync(); Check(input.KeyboardCalls == 1, "immediate start");
        clock.Advance(10); await engine.TickAsync(); Check(engine.GetStatus(first.Id).Count == 2 && engine.GetStatus(second.Id).Count == 0, "timer independence");
        clock.Advance(20); await engine.TickAsync(); Check(engine.GetStatus(second.Id).Count == 1, "second timer");
    }),
    ("Pause resumes remaining time, stop resets", async () =>
    {
        var (engine, action, input, clock) = Harness(); Start(engine);
        clock.Advance(100); engine.Pause(); clock.Advance(900); Start(engine);
        Check(Math.Abs(engine.GetStatus(action.Id).RemainingSeconds - 200) < .01, "remaining not preserved");
        clock.Advance(199); await engine.TickAsync(); Check(input.KeyboardCalls == 0, "early resume");
        clock.Advance(1); await engine.TickAsync(); Check(input.KeyboardCalls == 1, "resume did not execute");
        engine.Stop(); Check(engine.GetStatus(action.Id).State == "Stopped" && engine.GetStatus(action.Id).Count == 0, "stop reset");
    }),
    ("Wall clock changes never change monotonic timers", async () =>
    {
        var (engine, _, input, clock) = Harness(); Start(engine);
        clock.WallOffset = TimeSpan.FromDays(5); await engine.TickAsync(); Check(input.KeyboardCalls == 0, "wall jump ran action");
        clock.Advance(300); await engine.TickAsync(); Check(input.KeyboardCalls == 1, "monotonic deadline failed");
    }),
    ("Foreground switch prevents input", async () =>
    {
        var (engine, action, input, clock) = Harness(); Start(engine); input.Foreground = false;
        clock.Advance(300); await engine.TickAsync();
        Check(input.KeyboardCalls == 0 && engine.GetStatus(action.Id).State == "Faulted", "input leaked to wrong window");
    }),
    ("Invalid target detected before deadline", async () =>
    {
        var (engine, action, input, _) = Harness(); Start(engine); input.Valid = false;
        await engine.TickAsync(); Check(engine.GetStatus(action.Id).State == "Faulted" && input.KeyboardCalls == 0, "stale target ignored");
    }),
    ("Skip-cycle policy retries at next period", async () =>
    {
        var (engine, action, input, clock) = Harness(new AppSettings { WindowFailure = ErrorPolicy.SkipCycle }); Start(engine); input.Foreground = false;
        clock.Advance(300); await engine.TickAsync(); Check(engine.IsRunning && engine.GetStatus(action.Id).Count == 0, "skip did not remain running");
        input.Foreground = true; clock.Advance(299); await engine.TickAsync(); Check(input.KeyboardCalls == 0, "skip retried early");
        clock.Advance(1); await engine.TickAsync(); Check(input.KeyboardCalls == 1, "skip never retried");
    }),
    ("Pause all policy blocks other due actions", async () =>
    {
        var a = Action(); var b = Action(); a.RunImmediately = b.RunImmediately = true;
        var input = new FakeInput { ThrowInput = true };
        using var engine = new AutomationEngine(new List<AutomationAction> { a, b }, new AppSettings { InputFailure = ErrorPolicy.PauseAll }, input);
        Start(engine); await engine.TickAsync(); Check(!engine.IsRunning && input.KeyboardCalls == 1 && engine.GetStatus(b.Id).State == "Paused", "pause-all leaked input");
    }),
    ("Skip invalid window does not retry on every UI tick", async () =>
    {
        var (engine, _, input, clock) = Harness(new AppSettings { WindowFailure = ErrorPolicy.SkipCycle });
        var failures = 0; engine.Faulted += _ => failures++; Start(engine); input.Valid = false;
        await engine.TickAsync(); Check(failures == 0, "skip evaluated before due");
        clock.Advance(300); await engine.TickAsync(); await engine.TickAsync();
        Check(failures == 1, "skip failure spammed");
    }),
    ("Mouse sequence preserves order and rechecks foreground", async () =>
    {
        var (engine, action, input, _) = Harness(); action.Kind = ActionKind.Mouse; action.RunImmediately = true; action.PointDelayMs = 0;
        action.Points = [new() { X = 10, Y = 20 }, new() { X = 30, Y = 40 }];
        input.AfterMouse = () => input.Foreground = false;
        Start(engine); await engine.TickAsync(); Check(input.Points.Count == 1 && input.Points[0] == "10,20", "second point sent to wrong window");
        Check(engine.GetStatus(action.Id).State == "Faulted", "lost foreground not reported");
    }),
    ("Stop cancels position delays promptly", async () =>
    {
        var (engine, action, input, _) = Harness(); action.Kind = ActionKind.Mouse; action.RunImmediately = true; action.PointDelayMs = 60000;
        action.Points = [new(), new()]; Start(engine); var tick = engine.TickAsync();
        Check(input.Points.Count == 1, "first point missing"); engine.Stop();
        await tick.WaitAsync(TimeSpan.FromSeconds(2)); Check(input.Points.Count == 1 && !engine.IsRunning, "stop emitted additional point");
    }),
    ("Concurrent ticks serialize input", async () =>
    {
        var (engine, action, input, _) = Harness(); action.RunImmediately = true;
        input.Block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Start(engine); var first = engine.TickAsync(); await engine.TickAsync(); Check(input.KeyboardCalls == 1, "overlapping emission");
        input.Block.SetResult(); await first;
    }),
    ("Random range and jitter honor configured bounds", async () =>
    {
        var action = Action(); action.IntervalMode = IntervalMode.Range; action.MinSeconds = 40; action.MaxSeconds = 80;
        using var engine = new AutomationEngine(new List<AutomationAction> { action }, new(), new FakeInput(), new ManualClock(), () => .25);
        Start(engine); Check(engine.GetStatus(action.Id).RemainingSeconds == 50, "range sample");
        engine.Stop(); action.IntervalMode = IntervalMode.Jitter; action.IntervalSeconds = 100; action.JitterSeconds = 20; Start(engine);
        Check(engine.GetStatus(action.Id).RemainingSeconds == 90, "jitter sample"); await Task.CompletedTask;
    }),
    ("Configuration round trip clears runtime handles and atomically replaces", async () =>
    {
        var directory = Path.Combine(Path.GetTempPath(), "AutoPilotInput-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.json");
        try
        {
            var config = new AppConfig(); config.Actions[0].TargetHandle = 123; config.Actions[0].TargetProcessId = 55;
            ConfigStore.Export(path, config); config.Actions[0].Name = "Saved"; ConfigStore.Export(path, config);
            var restored = ConfigStore.Import(path);
            Check(restored.Actions[0].Name == "Saved" && restored.Actions[0].TargetHandle == 0 && restored.Actions[0].TargetProcessId == 0, "unsafe config roundtrip");
            Check(Directory.GetFiles(directory).Length == 1, "temporary save files leaked");
            File.WriteAllText(path, "{\"Actions\":null}");
            var rejected = false; try { ConfigStore.Import(path); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "malformed config accepted");
        }
        finally
        {
            File.Delete(path);
            // Windows may retain a delete-pending file briefly while antivirus closes a handle.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Delete(directory); break; }
                catch (IOException) when (attempt < 5) { await Task.Delay(100); }
            }
        }
        await Task.CompletedTask;
    }),
    ("Windows INPUT structure has correct architecture size", async () =>
    {
        Check(NativeInput.GetInputSize() == (IntPtr.Size == 8 ? 40 : 28), "SendInput structure wrong size"); await Task.CompletedTask;
    })
};
var failed = 0;
foreach (var (name, test) in tests)
{
    try { await test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception error) { failed++; Console.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed. All input was simulated; no keys or clicks were sent to other applications.");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Start(AutomationEngine engine) { var errors = engine.Start(); Check(errors.Count == 0, string.Join("; ", errors)); }
static AutomationAction Action() => new() { Keys = [65], TargetHandle = 100, TargetProcessId = 1 };
static (AutomationEngine Engine, AutomationAction Action, FakeInput Input, ManualClock Clock) Harness(AppSettings? settings = null)
{
    var action = Action(); var input = new FakeInput(); var clock = new ManualClock();
    return (new AutomationEngine(new List<AutomationAction> { action }, settings ?? new(), input, clock), action, input, clock);
}
sealed class ManualClock : TimeProvider
{
    private long _ticks;
    public TimeSpan WallOffset { get; set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks).Add(WallOffset);
    public void Advance(double seconds) => _ticks += (long)(seconds * TimeSpan.TicksPerSecond);
}
sealed class FakeInput : IAutomationInput
{
    public bool Valid = true, Foreground = true, ThrowInput;
    public int KeyboardCalls;
    public List<string> Points = [];
    public Action? AfterMouse;
    public TaskCompletionSource? Block;
    public bool IsValidTarget(long handle, uint processId) => Valid && handle != 0 && processId != 0;
    public bool IsForeground(long handle) => Foreground;
    public string? ValidatePoint(long handle, CoordinateMode mode, ClickPoint point) => null;
    public async Task SendKeyboardAsync(IReadOnlyList<int> keys, CancellationToken cancellationToken)
    {
        KeyboardCalls++;
        if (ThrowInput) throw new AutomationInputException(InputErrorKind.Input, "test rejection");
        if (Block is not null) await Block.Task.WaitAsync(cancellationToken);
    }
    public Task SendMouseAsync(long handle, CoordinateMode mode, ClickPoint point, MouseButton button, bool doubleClick, CancellationToken cancellationToken)
    {
        Points.Add($"{point.X},{point.Y}"); AfterMouse?.Invoke(); return Task.CompletedTask;
    }
}
