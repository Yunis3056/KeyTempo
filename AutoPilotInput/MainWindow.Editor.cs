using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoPilotInput.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutoPilotInput;

public partial class MainWindow
{
    private List<int> _draftKeys = [];
    private IntervalMode _formIntervalMode;

    private void LoadForm()
    {
        var previousLoading = _loading;
        _loading = true;
        _saveTimer.Stop();
        _dirty = false;
        _recordKey = false;
        try
        {
            Editor.IsEnabled = _selected is not null;
            if (_selected is null) return;
            NameBox.Text = _selected.Name;
            EnabledCheck.IsChecked = _selected.Enabled;
            _draftKeys = [.. _selected.Keys];
            RefreshKeyChoice();
            KeyboardPanel.Visibility = _selected.Kind == ActionKind.Keyboard ? Visibility.Visible : Visibility.Collapsed;
            MousePanel.Visibility = _selected.Kind == ActionKind.Mouse ? Visibility.Visible : Visibility.Collapsed;
            MouseButtonCombo.SelectedIndex = (int)_selected.MouseButton;
            ClickTypeCombo.SelectedIndex = _selected.DoubleClick ? 1 : 0;
            CoordinateCombo.SelectedIndex = (int)_selected.CoordinateMode;
            PointDelayBox.Text = _selected.PointDelayMs.ToString(CultureInfo.InvariantCulture);
            IntervalModeCombo.SelectedIndex = (int)_selected.IntervalMode;
            FirstRunCombo.SelectedIndex = _selected.RunImmediately ? 1 : 0;
            LoadInterval();
            RefreshPoints();
            TargetText.Text = _selected.TargetHandle != 0
                ? _selected.TargetTitle
                : string.IsNullOrWhiteSpace(_selected.TargetTitle)
                    ? L("未绑定 · 点击右侧，然后切到目标窗口", "Not bound · click Bind target, then switch windows")
                    : L("需要重新绑定：", "Rebind target: ") + _selected.TargetTitle;
            SaveStatus.Text = App.IsPreview ? L("界面预览", "Preview") : L("自动保存", "Autosave");
        }
        finally { _loading = previousLoading; }
    }

    private void LoadInterval()
    {
        if (_selected is null) return;
        var previousLoading = _loading;
        _loading = true;
        try
        {
            _formIntervalMode = (IntervalMode)Math.Max(0, IntervalModeCombo.SelectedIndex);
            SecondIntervalPanel.Visibility = _formIntervalMode == IntervalMode.Fixed ? Visibility.Collapsed : Visibility.Visible;
            IntervalLabel.Text = _formIntervalMode switch
            {
                IntervalMode.Range => L("最小间隔（秒）", "Minimum interval (seconds)"),
                IntervalMode.Jitter => L("中心间隔（秒）", "Center interval (seconds)"),
                _ => L("间隔（秒）", "Interval (seconds)")
            };
            SecondIntervalLabel.Text = _formIntervalMode == IntervalMode.Range ? L("最大间隔（秒）", "Maximum interval (seconds)") : L("随机幅度（秒）", "Jitter (seconds)");
            IntervalBox.Text = (_formIntervalMode == IntervalMode.Range ? _selected.MinSeconds : _selected.IntervalSeconds).ToString(CultureInfo.InvariantCulture);
            SecondIntervalBox.Text = (_formIntervalMode == IntervalMode.Range ? _selected.MaxSeconds : _selected.JitterSeconds).ToString(CultureInfo.InvariantCulture);
        }
        finally { _loading = previousLoading; }
    }

    private void QueueSave()
    {
        if (_loading || _selected is null) return;
        _dirty = true;
        SaveStatus.Text = L("等待保存…", "Unsaved changes…");
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private bool SaveForm(bool showError)
    {
        if (_loading || _selected is null || !_dirty) return true;
        _saveTimer.Stop();
        bool Fail(string error)
        {
            SaveStatus.Text = L("请检查输入", "Check your input");
            if (showError) ShowToast(error);
            return false;
        }
        static bool ParseNumber(string text, out double value) =>
            (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
             || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) && double.IsFinite(value);
        if (!ParseNumber(IntervalBox.Text, out var first)) return Fail(L("请输入有效的计时间隔（秒）。", "Enter a valid interval in seconds."));
        var mode = (IntervalMode)Math.Max(0, IntervalModeCombo.SelectedIndex);
        var second = _selected.JitterSeconds;
        if (mode != IntervalMode.Fixed && !ParseNumber(SecondIntervalBox.Text, out second))
            return Fail(L("请输入有效的最大间隔或随机幅度。", "Enter a valid maximum interval or jitter amount."));
        var delay = _selected.PointDelayMs;
        if (_selected.Kind == ActionKind.Mouse && !int.TryParse(PointDelayBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out delay))
            return Fail(L("位置之间的停顿必须是整数毫秒。", "The delay between points must be a whole number of milliseconds."));
        var coordinates = (CoordinateMode)Math.Max(0, CoordinateCombo.SelectedIndex);
        if (_selected.Points.Count > 0 && coordinates != _selected.CoordinateMode)
            return Fail(L("请先移除已有位置，再切换坐标模式。", "Remove existing points before changing coordinate mode."));

        var candidate = new AutomationAction
        {
            Id = _selected.Id, Name = NameBox.Text.Trim(), Enabled = EnabledCheck.IsChecked == true, Kind = _selected.Kind,
            Keys = [.. _draftKeys], IntervalMode = mode,
            IntervalSeconds = mode == IntervalMode.Range ? _selected.IntervalSeconds : first,
            MinSeconds = mode == IntervalMode.Range ? first : _selected.MinSeconds,
            MaxSeconds = mode == IntervalMode.Range ? second : _selected.MaxSeconds,
            JitterSeconds = mode == IntervalMode.Jitter ? second : _selected.JitterSeconds,
            RunImmediately = FirstRunCombo.SelectedIndex == 1, CoordinateMode = coordinates,
            MouseButton = (Core.MouseButton)Math.Max(0, MouseButtonCombo.SelectedIndex), DoubleClick = ClickTypeCombo.SelectedIndex == 1,
            PointDelayMs = delay, Points = _selected.Points,
            TargetHandle = _selected.TargetHandle, TargetProcessId = _selected.TargetProcessId, TargetTitle = _selected.TargetTitle
        };
        var errors = NativeInput.ValidateAction(candidate, requireInput: false);
        if (errors.Count > 0) return Fail(L(string.Join("\n", errors), "Invalid action settings. Check the name, intervals (0.1 seconds–7 days), keys and mouse points."));
        var changed = candidate.Name != _selected.Name || candidate.Enabled != _selected.Enabled || !candidate.Keys.SequenceEqual(_selected.Keys)
            || candidate.IntervalMode != _selected.IntervalMode || candidate.IntervalSeconds != _selected.IntervalSeconds
            || candidate.MinSeconds != _selected.MinSeconds || candidate.MaxSeconds != _selected.MaxSeconds || candidate.JitterSeconds != _selected.JitterSeconds
            || candidate.RunImmediately != _selected.RunImmediately || candidate.CoordinateMode != _selected.CoordinateMode
            || candidate.MouseButton != _selected.MouseButton || candidate.DoubleClick != _selected.DoubleClick || candidate.PointDelayMs != _selected.PointDelayMs;
        if (changed)
        {
            ResetForActionEdit();
            _selected.Name = candidate.Name; _selected.Enabled = candidate.Enabled; _selected.Keys = candidate.Keys;
            _selected.IntervalMode = candidate.IntervalMode; _selected.IntervalSeconds = candidate.IntervalSeconds;
            _selected.MinSeconds = candidate.MinSeconds; _selected.MaxSeconds = candidate.MaxSeconds; _selected.JitterSeconds = candidate.JitterSeconds;
            _selected.RunImmediately = candidate.RunImmediately; _selected.CoordinateMode = candidate.CoordinateMode;
            _selected.MouseButton = candidate.MouseButton; _selected.DoubleClick = candidate.DoubleClick; _selected.PointDelayMs = candidate.PointDelayMs;
        }
        if (!SaveConfiguration()) return false;
        _dirty = false;
        UpdateRows();
        UpdateStatus();
        return true;
    }

    private bool SaveConfiguration()
    {
        if (App.IsPreview) { SaveStatus.Text = L("界面预览", "Preview"); return true; }
        try
        {
            SettingsStore.Save(_settings);
            SaveStatus.Text = L("已自动保存", "Saved automatically");
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            SaveStatus.Text = L("保存失败", "Save failed");
            Log(L("保存配置失败：", "Could not save configuration: ") + error.Message);
            ShowToast(L("配置未保存：", "Configuration was not saved: ") + error.Message);
            return false;
        }
    }

    private void ResetForActionEdit()
    {
        if (_engine is not null && _settings.Actions.Any(action => _engine.GetStatus(action.Id).State != "Stopped")) _engine.Stop();
        if (_pendingStart) { _operation++; _pendingStart = false; }
    }

    private void FormTextChanged(object sender, TextChangedEventArgs e) => QueueSave();
    private void FormChanged(object sender, RoutedEventArgs e) => QueueSave();
    private void FormSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selected is null) return;
        if (ReferenceEquals(sender, CoordinateCombo) && _selected.Points.Count > 0 && CoordinateCombo.SelectedIndex != (int)_selected.CoordinateMode)
        {
            var previousLoading = _loading; _loading = true;
            CoordinateCombo.SelectedIndex = (int)_selected.CoordinateMode;
            _loading = previousLoading;
            ShowToast(L("请先移除已有位置，再切换坐标模式。", "Remove existing points before changing coordinate mode."));
            return;
        }
        QueueSave();
    }

    private void IntervalModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selected is null || IntervalModeCombo.SelectedIndex < 0) return;
        var nextMode = IntervalModeCombo.SelectedIndex;
        _loading = true; IntervalModeCombo.SelectedIndex = (int)_formIntervalMode; _loading = false;
        if (!SaveForm(true)) return;
        _loading = true; IntervalModeCombo.SelectedIndex = nextMode; LoadInterval(); _loading = false;
        QueueSave();
    }

    private void RefreshKeyChoice()
    {
        var previousLoading = _loading; _loading = true;
        try
        {
            var options = (KeyCombo.ItemsSource as IEnumerable<KeyOption> ?? []).Where(option => option.Key != -1).ToList();
            KeyOption? selected;
            if (_draftKeys.Count > 1)
            {
                selected = new KeyOption(-1, string.Join(" + ", _draftKeys.Select(KeyName)));
                options.Add(selected);
            }
            else
            {
                var key = _draftKeys.FirstOrDefault();
                selected = options.FirstOrDefault(option => option.Key == key);
                if (selected is null) { selected = new KeyOption(key, KeyName(key)); options.Add(selected); }
            }
            KeyCombo.ItemsSource = options; KeyCombo.SelectedItem = selected;
            RecordButton.Content = L("⌨  录制按键", "⌨  Record keys");
            KeyHint.Text = _draftKeys.Count == 0 ? L("尚未选择按键", "No key selected") : string.Join(" + ", _draftKeys.Select(KeyName));
        }
        finally { _loading = previousLoading; }
    }

    private void KeyComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _selected is null || KeyCombo.SelectedItem is not KeyOption option || option.Key == -1) return;
        if (option.Key > 0 && IsReserved(option.Key))
        {
            RefreshKeyChoice();
            ShowToast(L("此键已用作全局快捷键，请选择其他按键或先修改快捷键。", "This key is reserved for a global shortcut. Choose another key or change your shortcuts first."));
            return;
        }
        _recordKey = false;
        _draftKeys = option.Key == 0 ? [] : [option.Key];
        RefreshKeyChoice(); QueueSave();
    }

    private bool IsReserved(int key) => (_hotkeys?.ReservedVirtualKeys.Count > 0 ? _hotkeys.ReservedVirtualKeys : new[] { 0x75, 0x76, 0x77, 0x78 }).Contains(key);

    private void RecordKeyClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Kind != ActionKind.Keyboard) return;
        if (_recordKey) { _recordKey = false; RefreshKeyChoice(); return; }
        FinishCapture(false);
        if (_engine.IsRunning) { _engine.Pause(); UpdateStatus(); }
        _recordKey = true;
        RecordButton.Content = L("取消录制", "Cancel recording");
        KeyHint.Text = L("请按单键或组合键；Esc 取消。", "Press a key or shortcut; Esc cancels.");
        Keyboard.Focus(RecordButton);
    }

    private void WindowKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recordKey) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key == Key.Escape) { _recordKey = false; RefreshKeyChoice(); return; }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey < 8 || virtualKey > 254) return;
        var keys = new List<int>();
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control)) keys.Add(17);
        if (modifiers.HasFlag(ModifierKeys.Alt)) keys.Add(18);
        if (modifiers.HasFlag(ModifierKeys.Shift)) keys.Add(16);
        if (modifiers.HasFlag(ModifierKeys.Windows)) keys.Add(91);
        keys.Add(virtualKey);
        if (keys.Any(IsReserved))
        {
            ShowToast(L("录制的按键与全局快捷键冲突，请换一个按键。", "This key conflicts with a global shortcut. Press a different key."));
            return;
        }
        _draftKeys = keys;
        _recordKey = false;
        RefreshKeyChoice(); QueueSave(); SaveForm(false);
    }

    private void AddKeyboardClick(object sender, RoutedEventArgs e) => AddAction(ActionKind.Keyboard);
    private void AddMouseClick(object sender, RoutedEventArgs e) => AddAction(ActionKind.Mouse);
    private void AddAction(ActionKind kind)
    {
        if (!SaveForm(true)) return;
        if (_settings.Actions.Count >= 100) { ShowToast(L("最多支持 100 个动作。", "You can create up to 100 actions.")); return; }
        FinishCapture(false); ResetForActionEdit();
        var action = NewAction(kind);
        _settings.Actions.Add(action); RenderActions(action.Id); SaveConfiguration(); UpdateStatus();
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _saveTimer.Stop(); FinishCapture(false); ResetForActionEdit();
        var index = _settings.Actions.IndexOf(_selected);
        _settings.Actions.Remove(_selected);
        if (_settings.Actions.Count == 0) _settings.Actions.Add(NewAction(ActionKind.Keyboard));
        _dirty = false;
        RenderActions(_settings.Actions[Math.Clamp(index, 0, _settings.Actions.Count - 1)].Id);
        SaveConfiguration(); UpdateStatus();
    }

    private void RefreshPoints(int? selectIndex = null)
    {
        var selection = selectIndex ?? PointsList.SelectedIndex;
        PointsList.Items.Clear();
        if (_selected is null) return;
        for (var index = 0; index < _selected.Points.Count; index++)
        {
            var point = _selected.Points[index];
            PointsList.Items.Add($"{index + 1:00}     X {point.X}     Y {point.Y}");
        }
        if (PointsList.Items.Count > 0) PointsList.SelectedIndex = Math.Clamp(selection, 0, PointsList.Items.Count - 1);
    }

    private void RemovePointClick(object sender, RoutedEventArgs e)
    {
        if (_selected is null || PointsList.SelectedIndex < 0 || !SaveForm(true)) return;
        var index = PointsList.SelectedIndex;
        ResetForActionEdit(); _selected.Points.RemoveAt(index);
        RefreshPoints(index); SaveConfiguration(); UpdateRows(); UpdateStatus();
    }
    private void MovePointUpClick(object sender, RoutedEventArgs e) => MovePoint(-1);
    private void MovePointDownClick(object sender, RoutedEventArgs e) => MovePoint(1);
    private void MovePoint(int direction)
    {
        if (_selected is null) return;
        var index = PointsList.SelectedIndex;
        var destination = index + direction;
        if (index < 0 || destination < 0 || destination >= _selected.Points.Count || !SaveForm(true)) return;
        ResetForActionEdit();
        (_selected.Points[index], _selected.Points[destination]) = (_selected.Points[destination], _selected.Points[index]);
        RefreshPoints(destination); SaveConfiguration(); UpdateRows(); UpdateStatus();
    }
}
