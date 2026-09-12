namespace AutoPilotInput.Core;

/// <summary>Independent monotonic timers with one serialized input stream.</summary>
public sealed class AutomationEngine : IDisposable
{
    private sealed class Run
    {
        public required AutomationAction Action;
        public string State = "Stopped";
        public long Due;
        public double Remaining;
        public int Count;
        public DateTimeOffset? LastRun;
        public string Error = "";
    }
    private readonly IList<AutomationAction> _actions;
    private readonly AppSettings _settings;
    private readonly IAutomationInput _input;
    private readonly TimeProvider _clock;
    private readonly Func<double> _random;
    private readonly Dictionary<Guid, Run> _runs = [];
    private readonly SemaphoreSlim _tickGate = new(1, 1);
    private readonly object _sync = new();
    private CancellationTokenSource _cancellation = new();
    private long _generation;
    private bool _disposed;
    public event Action<string>? Log;
    public event Action<AutomationAction>? Executed;
    public event Action<string>? Faulted;
    public bool IsRunning { get { lock (_sync) return _runs.Values.Any(run => run.State == "Running"); } }

    public AutomationEngine(IList<AutomationAction> actions, AppSettings settings)
        : this(actions, settings, new WindowsAutomationInput(), TimeProvider.System, Random.Shared.NextDouble) { }
    public AutomationEngine(IList<AutomationAction> actions, AppSettings settings, IAutomationInput input, TimeProvider? clock = null, Func<double>? random = null)
    {
        _actions = actions; _settings = settings; _input = input;
        _clock = clock ?? TimeProvider.System; _random = random ?? Random.Shared.NextDouble;
    }

    public List<string> Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var errors = new List<string>();
        lock (_sync)
        {
            var enabled = _actions.Where(action => action.Enabled).ToList();
            if (enabled.Count == 0) errors.Add("请至少启用一个动作。");
            if (_actions.Select(action => action.Id).Distinct().Count() != _actions.Count) errors.Add("动作标识重复，请重新导入有效配置。");
            foreach (var action in enabled)
            {
                errors.AddRange(NativeInput.ValidateAction(action).Select(error => $"{action.Name}: {error}"));
                if (!_input.IsValidTarget(action.TargetHandle, action.TargetProcessId)) errors.Add($"{action.Name}: 请先锁定一个有效目标窗口。");
            }
            if (errors.Count > 0) return errors;
            if (_cancellation.IsCancellationRequested) _cancellation = new CancellationTokenSource();
            var now = _clock.GetTimestamp();
            var active = enabled.Select(action => action.Id).ToHashSet();
            foreach (var id in _runs.Keys.Where(id => !active.Contains(id)).ToList()) _runs.Remove(id);
            foreach (var action in enabled)
            {
                if (_runs.TryGetValue(action.Id, out var run) && run.State == "Running") continue;
                if (run is null)
                {
                    run = new Run { Action = Snapshot(action), Remaining = action.RunImmediately ? 0 : SampleInterval(action) };
                    _runs[action.Id] = run;
                }
                else
                {
                    run.Action = Snapshot(action);
                    if (run.State == "Stopped") run.Remaining = action.RunImmediately ? 0 : SampleInterval(action);
                }
                run.Due = AddSeconds(now, run.Remaining);
                run.State = "Running";
                run.Error = "";
            }
        }
        Log?.Invoke("动作已开始 / 已继续。");
        return errors;
    }

    public void Pause()
    {
        lock (_sync)
        {
            _generation++;
            _cancellation.Cancel();
            foreach (var run in _runs.Values.Where(run => run.State == "Running"))
            {
                run.Remaining = Remaining(run);
                run.State = "Paused";
            }
        }
        Log?.Invoke("已暂停，保留剩余计时。");
    }
    public void Stop()
    {
        lock (_sync)
        {
            _generation++;
            _cancellation.Cancel();
            _runs.Clear();
        }
        Log?.Invoke("已停止并重置计时与执行次数。");
    }
    public ActionStatus GetStatus(Guid id)
    {
        lock (_sync)
        {
            if (!_runs.TryGetValue(id, out var run)) return new ActionStatus();
            var remaining = run.State == "Running" ? Remaining(run) : run.Remaining;
            return new ActionStatus
            {
                State = run.State, RemainingSeconds = remaining, Count = run.Count, LastRun = run.LastRun,
                NextRun = run.State == "Running" ? _clock.GetUtcNow().AddSeconds(remaining) : null, Error = run.Error
            };
        }
    }

    public async Task TickAsync()
    {
        if (_disposed || !await _tickGate.WaitAsync(0)) return;
        try
        {
            List<Run> snapshot; long generation; CancellationToken token;
            lock (_sync) { snapshot = _runs.Values.Where(run => run.State == "Running").ToList(); generation = _generation; token = _cancellation.Token; }
            foreach (var run in snapshot)
            {
                bool due;
                lock (_sync)
                {
                    if (token.IsCancellationRequested || generation != _generation) return;
                    if (run.State != "Running") continue;
                    due = Remaining(run) <= 0;
                }
                try
                {
                    // SkipCycle evaluates a missing window only at its next scheduled attempt.
                    // Pause policies still detect a closed target immediately while waiting.
                    if (!due && _settings.WindowFailure == ErrorPolicy.SkipCycle) continue;
                    EnsureTarget(run.Action, checkForeground: due);
                    if (!due) continue;
                    if (run.Action.Kind == ActionKind.Keyboard)
                    {
                        token.ThrowIfCancellationRequested();
                        await _input.SendKeyboardAsync(run.Action.Keys, token);
                    }
                    else
                    {
                        for (var index = 0; index < run.Action.Points.Count; index++)
                        {
                            token.ThrowIfCancellationRequested();
                            EnsureTarget(run.Action, true);
                            var point = run.Action.Points[index];
                            var error = _input.ValidatePoint(run.Action.TargetHandle, run.Action.CoordinateMode, point);
                            if (error is not null) throw new AutomationInputException(InputErrorKind.InvalidPoint, error);
                            await _input.SendMouseAsync(run.Action.TargetHandle, run.Action.CoordinateMode, point, run.Action.MouseButton, run.Action.DoubleClick, token);
                            if (index + 1 < run.Action.Points.Count && run.Action.PointDelayMs > 0)
                                await Task.Delay(TimeSpan.FromMilliseconds(run.Action.PointDelayMs), _clock, token);
                        }
                    }
                    lock (_sync)
                    {
                        if (token.IsCancellationRequested || generation != _generation || run.State != "Running") return;
                        run.Count++;
                        run.LastRun = _clock.GetUtcNow();
                        ScheduleNext(run);
                    }
                    Log?.Invoke($"{run.Action.Name}: 已执行第 {run.Count} 次。");
                    Executed?.Invoke(run.Action);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (AutomationInputException error) { HandleFailure(run, error, generation); }
                catch (Exception error) { HandleFailure(run, new AutomationInputException(InputErrorKind.Input, error.Message), generation); }
            }
        }
        finally { _tickGate.Release(); }
    }
    private void EnsureTarget(AutomationAction action, bool checkForeground)
    {
        if (!_input.IsValidTarget(action.TargetHandle, action.TargetProcessId)) throw new AutomationInputException(InputErrorKind.Window, "目标窗口已关闭或失效，请重新锁定窗口。");
        if (checkForeground && !_input.IsForeground(action.TargetHandle)) throw new AutomationInputException(InputErrorKind.Window, "目标窗口当前不在前台；请切回目标窗口后继续。");
    }
    private void HandleFailure(Run run, AutomationInputException error, long generation)
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            var policy = error.Kind switch { InputErrorKind.Window => _settings.WindowFailure, InputErrorKind.InvalidPoint => _settings.InvalidPoint, _ => _settings.InputFailure };
            run.Error = error.Message;
            if (policy == ErrorPolicy.SkipCycle) ScheduleNext(run);
            else
            {
                if (policy == ErrorPolicy.PauseAll)
                {
                    _generation++;
                    _cancellation.Cancel();
                    foreach (var other in _runs.Values.Where(item => item.State == "Running")) { other.Remaining = Remaining(other); other.State = "Paused"; }
                }
                run.Remaining = Remaining(run);
                run.State = "Faulted";
            }
        }
        var message = $"{run.Action.Name}: {error.Message}";
        Log?.Invoke(message);
        Faulted?.Invoke(message);
    }
    private double Remaining(Run run) => Math.Max(0, (run.Due - _clock.GetTimestamp()) / (double)_clock.TimestampFrequency);
    private long AddSeconds(long timestamp, double seconds) => checked(timestamp + (long)(seconds * _clock.TimestampFrequency));
    private double SampleInterval(AutomationAction action)
    {
        var unit = Math.Clamp(_random(), 0, 1);
        return action.IntervalMode switch
        {
            IntervalMode.Range => action.MinSeconds + unit * (action.MaxSeconds - action.MinSeconds),
            IntervalMode.Jitter => action.IntervalSeconds - action.JitterSeconds + unit * action.JitterSeconds * 2,
            _ => action.IntervalSeconds
        };
    }
    private void ScheduleNext(Run run)
    {
        run.Remaining = SampleInterval(run.Action);
        run.Due = AddSeconds(_clock.GetTimestamp(), run.Remaining);
    }
    private static AutomationAction Snapshot(AutomationAction action) => new()
    {
        Id = action.Id, Name = action.Name, Enabled = action.Enabled, Kind = action.Kind, Keys = [.. action.Keys],
        IntervalMode = action.IntervalMode, IntervalSeconds = action.IntervalSeconds, MinSeconds = action.MinSeconds,
        MaxSeconds = action.MaxSeconds, JitterSeconds = action.JitterSeconds, RunImmediately = action.RunImmediately,
        CoordinateMode = action.CoordinateMode, Points = action.Points.Select(point => new ClickPoint { X = point.X, Y = point.Y }).ToList(),
        MouseButton = action.MouseButton, DoubleClick = action.DoubleClick, PointDelayMs = action.PointDelayMs,
        TargetHandle = action.TargetHandle, TargetProcessId = action.TargetProcessId, TargetTitle = action.TargetTitle
    };
    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        // TickAsync may still be unwinding a key release; it retains its cancellation token and semaphore.
    }
}
