using AutoPilotInput.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace AutoPilotInput;
public partial class MainWindow
{
    private void HotkeyPressed(int id)
    {
        if(id==HotkeyService.StopId){StopAll();return;}
        if(id==HotkeyService.CaptureId){CapturePoint();return;}
        if(id==HotkeyService.FinishCaptureId){FinishCapture(true);return;}
        if(_recordKey){_recordKey=false;RecordButton.Content=L("⌨  录制按键","⌨  Record keys");ShowToast(L("此键用于控制程序，未录制。","This key controls KeyTempo and was not recorded."));return;}
        if(id==HotkeyService.StartId)Toggle(false);
    }
    private void StartClick(object sender,RoutedEventArgs e)=>Toggle(true);
    private async void Toggle(bool fromUi)
    {
        if(App.IsPreview||_pendingStart)return;
        if(_engine.IsRunning){_engine.Pause();UpdateStatus();return;}
        if(!_hotkeysReady){ShowToast(L("请先在设置中注册有效的停止快捷键。","Register working global shortcuts in Settings first."));return;}
        if(!SaveForm(true))return;
        if(_settings.Actions.Where(a=>a.Enabled&&a.Kind==ActionKind.Keyboard).SelectMany(a=>a.Keys).Any(IsReserved)){ShowToast(L("动作包含全局快捷键，请修改按键或快捷键设置。","An action uses a reserved shortcut key. Change it before starting."));return;}
        FinishCapture(false);_recordKey=false;
        var errors=_settings.Actions.Where(a=>a.Enabled).SelectMany(a=>NativeInput.ValidateAction(a).Select(x=>a.Name+": "+x)).ToList();
        foreach(var a in _settings.Actions.Where(a=>a.Enabled&&!NativeInput.IsValidTarget(a.TargetHandle,a.TargetProcessId)))errors.Add(a.Name+L(": 请先绑定目标窗口。",": Bind a target first."));
        if(!_settings.Actions.Any(a=>a.Enabled))errors.Add(L("请至少启用一个动作。","Enable at least one action."));
        if(errors.Count>0){ShowToast(string.Join("\n",errors.Take(3)));return;}
        if(fromUi)
        {
            _pendingStart=true;Editor.IsEnabled=false;var operation=++_operation;
            for(var i=3;i>0;i--){ShowToast(L($"请切换到目标窗口 · {i} 秒后开始",$"Switch to the target · starting in {i}s"));await Task.Delay(1000);if(operation!=_operation||_closing){_pendingStart=false;UpdateStatus();return;}}
            _pendingStart=false;
        }
        errors=_engine.Start();if(errors.Count>0)ShowToast(string.Join("\n",errors.Take(3)));UpdateStatus();
    }
    private void StopClick(object sender,RoutedEventArgs e)=>StopAll();
    private void StopAll(){_operation++;_pendingStart=false;_recordKey=false;FinishCapture(false);_engine.Stop();UpdateStatus();}
    public void EmergencyStop(){_operation++;_pendingStart=false;_engine?.Stop();}
    private async void UiTick(object? sender,EventArgs e)
    {
        UpdateStatus(); if(_tickBusy||_closing)return;_tickBusy=true;
        try{await _engine.TickAsync();}catch(Exception ex){_engine.Stop();Log(ex.Message);ShowToast(ex.Message);}finally{_tickBusy=false;}
    }
    private void UpdateRows()
    {
        foreach(var a in _settings.Actions)
        {
            if(!_rows.TryGetValue(a.Id,out var row))continue;row.Name.Text=a.Name;
            var input=a.Kind==ActionKind.Keyboard?a.Keys.Count==0?L("未选择按键","No key selected"):KeysName(a):L($"{a.Points.Count} 个位置",$"{a.Points.Count} positions");
            var interval=a.IntervalMode==IntervalMode.Fixed?$"{a.IntervalSeconds:0.##} s":a.IntervalMode==IntervalMode.Range?$"{a.MinSeconds:0.##}–{a.MaxSeconds:0.##} s":$"{a.IntervalSeconds:0.##} ± {a.JitterSeconds:0.##} s";row.Info.Text=input+"  ·  "+interval;
            var s=_engine.GetStatus(a.Id);
            row.State.Text=!a.Enabled?L("○ 已禁用","○ Disabled"):s.State=="Running"?"● "+TimeText(s.RemainingSeconds)+L($"  ·  {s.Count} 次",$"  ·  {s.Count} runs"):s.State=="Faulted"?L("● 已暂停 · 检查目标","● Paused · check target"):s.State=="Paused"?L("○ 已暂停","○ Paused"):a.TargetHandle==0?L("○ 等待绑定目标","○ Awaiting target"):L("○ 已就绪","○ Ready");
            row.State.SetResourceReference(TextBlock.ForegroundProperty,s.State=="Running"?"Success":"Muted");
        }
    }
    private static string TimeText(double value){var span=TimeSpan.FromSeconds(Math.Max(0,Math.Ceiling(value)));return span.TotalHours>=1?$"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}":$"{span.Minutes:00}:{span.Seconds:00}";}
    private void UpdateStatus()
    {
        var statuses=_settings.Actions.Select(a=>_engine.GetStatus(a.Id)).ToList();var running=statuses.Where(s=>s.State=="Running").ToList();var paused=statuses.Any(s=>s.State is "Paused" or "Faulted");
        var state=running.Count>0?L($"{running.Count} 个动作运行中",$"{running.Count} actions running"):paused?L("已暂停","Paused"):L("已就绪","Ready");
        StatusText.Text=state;StartButton.Content=running.Count>0?L("Ⅱ  暂停全部","Ⅱ  Pause all"):paused?L("▶  继续全部","▶  Resume all"):L("▶  开始全部","▶  Start all");
        StateDot.Fill=new SolidColorBrush(running.Count>0?System.Windows.Media.Color.FromRgb(112,229,188):System.Windows.Media.Color.FromRgb(175,163,203));
        var next=running.OrderBy(s=>s.RemainingSeconds).FirstOrDefault();
        CountdownText.Text=next is not null?TimeText(next.RemainingSeconds):paused?TimeText(statuses.First(s=>s.State is "Paused" or "Faulted").RemainingSeconds):TimeText(_selected?.IntervalSeconds??300);
        NextText.Text=next?.NextRun is not null?L("下一次执行  ","Next action  ")+next.NextRun.Value.LocalDateTime.ToString("HH:mm:ss"):paused?L("剩余计时已保留","Remaining time preserved"):L("配置完成后，即可启程","Ready when you are");
        CountText.Text=statuses.Sum(s=>s.Count).ToString();var last=statuses.Where(s=>s.LastRun.HasValue).MaxBy(s=>s.LastRun);LastText.Text=L("最近执行 ","Last run ")+(last?.LastRun?.LocalDateTime.ToString("HH:mm:ss")??"—");
        Editor.IsEnabled=_selected is not null&&running.Count==0&&!_pendingStart;HotkeyText.Text=$"{_settings.StartHotkey}  "+L("开始 / 暂停","Start / pause")+$"     {_settings.StopHotkey}  "+L("停止","Stop")+"     ·     MIT";FooterText.Text="●  "+L("离线运行","Offline")+"   ·   "+state;
        if(_tray is not null&&_trayState!=state){_trayState=state;var text="KeyTempo · "+state;_tray.Text=text[..Math.Min(63,text.Length)];}
        UpdateRows();
    }
    private void CreateTray()
    {
        DisposeTray();
        _trayIcon ??= LoadTrayIcon();
        _trayState="";
        _tray=new System.Windows.Forms.NotifyIcon{Icon=_trayIcon,Text="KeyTempo",Visible=true};
        var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add(L("显示窗口","Show window"),null,(_,_)=>ShowMain());menu.Items.Add(L("开始 / 暂停","Start / pause"),null,(_,_)=>Toggle(false));menu.Items.Add(L("停止全部","Stop all"),null,(_,_)=>StopAll());menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());menu.Items.Add(L("退出","Exit"),null,(_,_)=>Close());_tray.ContextMenuStrip=menu;_tray.DoubleClick+=(_,_)=>ShowMain();
    }
    private static System.Drawing.Icon LoadTrayIcon()
    {
        // Read the embedded resource so portable single-file builds need no adjacent .ico file.
        using var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))!.Stream;
        using var icon=new System.Drawing.Icon(stream,System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)icon.Clone();
    }
    private void DisposeTray()
    {
        if(_tray is null)return;
        var menu=_tray.ContextMenuStrip;
        _tray.Visible=false;_tray.Dispose();menu?.Dispose();_tray=null;
    }
    private void Notify(string title,string message,bool important)
    {
        if(_tray is null||!_settings.Notifications)return;if(!important&&(DateTime.Now-_lastNotification).TotalSeconds<30)return;_lastNotification=DateTime.Now;_tray.ShowBalloonTip(3000,title,message,important?System.Windows.Forms.ToolTipIcon.Warning:System.Windows.Forms.ToolTipIcon.Info);
    }
}
