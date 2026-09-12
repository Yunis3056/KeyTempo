using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoPilotInput.Core;
using CoreSettings = AutoPilotInput.Core.AppSettings;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
namespace AutoPilotInput;
public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private readonly CoreSettings _engineSettings = new();
    private AutomationEngine _engine = null!;
    private AutomationAction? _selected;
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly Dictionary<Guid, (TextBlock Name, TextBlock Info, TextBlock State)> _rows = [];
    private HotkeyService? _hotkeys;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _loading = true, _recordKey, _recordPoints, _closing, _tickBusy, _hotkeysReady, _pendingStart, _dirty;
    private int _operation, _toastVersion;
    private DateTime _lastNotification = DateTime.MinValue;
    private string _trayState = "";
    private record KeyOption(int Key, string Label) { public override string ToString() => Label; }
    private bool English => _settings.Language == "en-US";
    private string L(string zh, string en) => English ? en : zh;
    public MainWindow()
    {
        InitializeComponent(); Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico")); string? warning = null;
        _settings = App.IsPreview ? new AppSettings() : SettingsStore.Load(out warning);
        if (App.PreviewLight) _settings.Theme = "Light";
        if (_settings.Actions.Count == 0) _settings.Actions.Add(NewAction(ActionKind.Keyboard));
        InitializeEngine(); FillChoices(); LoadSettings(); ApplyAppearance(); RenderActions(_settings.Actions[0].Id);
        _loading = false; Loaded += OnLoaded; Closing += OnClosing;
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && !App.IsPreview) Hide(); };
        _uiTimer.Tick += UiTick; _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveForm(false); };
        Log(L("配置按键与目标窗口后即可开始。", "Choose a key and bind a target to begin.")); if (warning is not null) Log(warning);
    }
    private void InitializeEngine()
    {
        UpdatePolicies(); _engine = new AutomationEngine(_settings.Actions, _engineSettings);
        _engine.Log += message => Dispatcher.Invoke(() => Log(message));
        _engine.Executed += action => Dispatcher.Invoke(() => { if (_settings.Sound) System.Media.SystemSounds.Asterisk.Play(); Pulse(); Notify(L("动作已执行", "Action executed"), action.Name, false); });
        _engine.Faulted += message => Dispatcher.Invoke(() => { ShowToast(message); Notify(L("动作需要处理", "Action needs attention"), message, true); });
    }
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) => { if (m == App.ShowMessage) { ShowMain(); handled = true; } return IntPtr.Zero; });
        if (!App.IsPreview)
        {
            _hotkeys = new HotkeyService(source); _hotkeys.Pressed += HotkeyPressed;
            _hotkeysReady = _hotkeys.Configure(_settings.StartHotkey, _settings.StopHotkey, _settings.CaptureHotkey, out var error);
            if (!_hotkeysReady) ShowToast(error ?? L("快捷键注册失败", "Could not register shortcuts"));
            CreateTray(); _uiTimer.Start(); Microsoft.Win32.SystemEvents.PowerModeChanged += PowerModeChanged; Microsoft.Win32.SystemEvents.SessionSwitch += SessionChanged;
        }
        Translate(this); UpdateStatus();
        if (App.IsPreview && App.PreviewPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(App.PreviewPath)!);
            if (App.VerifyUi) VerifyInterface();
            PreparePreviewScenario();
            await Task.Delay(500); UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(this);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(this); Directory.CreateDirectory(Path.GetDirectoryName(App.PreviewPath)!);
            using (var file = File.Create(App.PreviewPath)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file); } Close();
        }
    }
    private void PowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { if (e.Mode == Microsoft.Win32.PowerModes.Suspend) Dispatcher.Invoke(() => { if (_engine.IsRunning) _engine.Pause(); }); }
    private void SessionChanged(object sender, Microsoft.Win32.SessionSwitchEventArgs e) { if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) Dispatcher.Invoke(() => { if (_engine.IsRunning) _engine.Pause(); }); }
    private AutomationAction NewAction(ActionKind kind) => new() { Kind = kind, Name = kind == ActionKind.Keyboard ? L("键盘动作", "Keyboard action") : L("鼠标动作", "Mouse action") };
    private void FillChoices()
    {
        _loading = true;
        SetChoices(IntervalModeCombo, [L("固定间隔", "Fixed interval"), L("随机 · 最小 / 最大", "Random · min / max"), L("随机 · 中心 / 幅度", "Random · center / jitter")]);
        SetChoices(FirstRunCombo, [L("等待一个周期", "Wait one interval"), L("立即执行", "Execute immediately")]);
        SetChoices(MouseButtonCombo, [L("鼠标左键", "Left button"), L("鼠标右键", "Right button")]);
        SetChoices(ClickTypeCombo, [L("单击", "Single click"), L("双击", "Double click")]);
        SetChoices(CoordinateCombo, [L("屏幕坐标", "Screen coordinates"), L("窗口相对坐标", "Window coordinates")]);
        SetChoices(ThemeCombo, [L("深色玻璃", "Dark glass"), L("浅色玻璃", "Light glass")]); SetChoices(LanguageCombo, ["简体中文", "English"]);
        SetChoices(MotionCombo, [L("省电", "Power saving"), L("标准", "Standard"), L("炫酷", "Fancy")]);
        SetChoices(TargetPolicyCombo, [L("暂停此动作", "Pause this action"), L("暂停全部", "Pause all"), L("跳过本周期", "Skip this cycle")]);
        SetChoices(PointPolicyCombo, [L("跳过本周期", "Skip this cycle"), L("暂停此动作", "Pause this action"), L("暂停全部", "Pause all")]);
        SetChoices(InputPolicyCombo, [L("暂停此动作", "Pause this action"), L("暂停全部", "Pause all"), L("跳过本周期", "Skip this cycle")]);
        var options = new List<KeyOption> { new(0, L("请选择按键…", "Choose a key…")) };
        options.AddRange(Enumerable.Range(65, 26).Select(v => new KeyOption(v, ((char)v).ToString()))); options.AddRange(Enumerable.Range(48, 10).Select(v => new KeyOption(v, ((char)v).ToString()))); options.AddRange(Enumerable.Range(112, 24).Select(v => new KeyOption(v, "F" + (v - 111))));
        options.AddRange(new[] { 8,9,13,16,17,18,19,20,27,32,33,34,35,36,37,38,39,40,44,45,46,91,92,96,97,98,99,100,101,102,103,104,105,106,107,109,110,111,144,145,186,187,188,189,190,191,192,219,220,221,222 }.Select(v => new KeyOption(v, KeyName(v)))); KeyCombo.ItemsSource = options;
    }
    private static void SetChoices(ComboBox combo, string[] values) { var index = combo.SelectedIndex; combo.ItemsSource = values; combo.SelectedIndex = Math.Max(0, Math.Min(index, values.Length - 1)); }
    private static string KeyName(int key) => key switch { 16 => "Shift", 17 => "Ctrl", 18 => "Alt", 91 => "Win", 32 => "Space", 13 => "Enter", _ => KeyInterop.KeyFromVirtualKey(key).ToString() };
    private static string KeysName(AutomationAction action) => string.Join(" + ", action.Keys.Select(KeyName));
    private void RenderActions(Guid? select = null)
    {
        var old = _loading; _loading = true; ActionList.Items.Clear(); _rows.Clear();
        foreach (var action in _settings.Actions)
        {
            var panel = new StackPanel(); var category = new TextBlock { Text = action.Kind == ActionKind.Keyboard ? "⌨   KEYBOARD" : "◎   MOUSE", FontSize = 10, Margin = new Thickness(0,0,0,10) }; category.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var name = new TextBlock { Text = action.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }; var info = new TextBlock { FontSize = 11, Margin = new Thickness(0,7,0,9) }; info.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); var state = new TextBlock { FontSize = 10 }; state.SetResourceReference(TextBlock.ForegroundProperty,"Muted");
            panel.Children.Add(category); panel.Children.Add(name); panel.Children.Add(info); panel.Children.Add(state); var item = new ListBoxItem { Tag = action.Id, Content = panel }; ActionList.Items.Add(item); _rows[action.Id] = (name, info, state); if (select == action.Id) ActionList.SelectedItem = item;
        }
        ActionTotal.Text = _settings.Actions.Count.ToString("00"); _selected = _settings.Actions.FirstOrDefault(a => a.Id == select) ?? _settings.Actions.FirstOrDefault(); if (ActionList.SelectedIndex < 0 && ActionList.Items.Count > 0) ActionList.SelectedIndex = 0; LoadForm(); _loading = old; UpdateRows();
    }
    private void ActionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ActionList.SelectedItem is not ListBoxItem item) return;
        if (!SaveForm(true)) { _loading=true; ActionList.SelectedItem=ActionList.Items.Cast<ListBoxItem>().FirstOrDefault(x=>(Guid)x.Tag==_selected?.Id); _loading=false; return; }
        _recordKey = false; FinishCapture(false); _selected = _settings.Actions.First(a => a.Id == (Guid)item.Tag); LoadForm();
    }
    private async void ShowToast(string text) { var version=++_toastVersion;ToastText.Text=text;Toast.Visibility=Visibility.Visible;Toast.Opacity=1;if(_settings.Effects!="PowerSaving")Toast.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(160)));await Task.Delay(4200);if(version==_toastVersion&&!_closing)Toast.Visibility=Visibility.Collapsed; }
    private void Pulse(){if(_settings.Effects=="PowerSaving")return;StateDot.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.25,1,TimeSpan.FromMilliseconds(380)));}
    private void Log(string text){LogList.Items.Insert(0,$"{DateTime.Now:HH:mm:ss}   {text}");while(LogList.Items.Count>200)LogList.Items.RemoveAt(LogList.Items.Count-1);}
    private void ShowMain(){Show();WindowState=WindowState.Normal;Activate();}
    private void DragWindow(object sender,MouseButtonEventArgs e){if(e.ChangedButton!=System.Windows.Input.MouseButton.Left)return;if(e.ClickCount==2){MaximizeClick(sender,e);return;}DragMove();}
    private void MaximizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void MinimizeClick(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void CloseClick(object sender,RoutedEventArgs e)=>Close();
    private async void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)
    {
        if(_closing){e.Cancel=true;return;}
        if(_tickBusy)
        {
            e.Cancel=true;_closing=true;_operation++;_engine.Stop();
            while(_tickBusy) await Task.Delay(10);
            _closing=false;Close();return;
        }
        _closing=true;_operation++;_uiTimer.Stop();_saveTimer.Stop();SaveForm(false);_engine.Dispose();_hotkeys?.Dispose();
        Microsoft.Win32.SystemEvents.PowerModeChanged-=PowerModeChanged;Microsoft.Win32.SystemEvents.SessionSwitch-=SessionChanged;
        if(_tray is not null){_tray.Visible=false;_tray.Dispose();}
    }
}
