using AutoPilotInput.Core;
using System.Windows;
namespace AutoPilotInput;
public partial class MainWindow
{
    private async void BindTargetClick(object sender,RoutedEventArgs e)
    {
        if(_selected is null||_engine.IsRunning||!SaveForm(true))return;
        var action=_selected;var operation=++_operation;BindButton.IsEnabled=false;
        for(var i=3;i>0;i--){ShowToast(L($"请切换到目标窗口 · {i} 秒后绑定",$"Switch to your target · binding in {i}s"));await Task.Delay(1000);if(operation!=_operation||_closing){BindButton.IsEnabled=true;return;}}
        var handle=NativeInput.GetForeground();var pid=NativeInput.GetProcessId(handle);
        if(pid==Environment.ProcessId||!NativeInput.IsValidTarget(handle,pid)){ShowToast(L("未绑定。请在倒计时结束前切换到其他程序。","Not bound. Switch to another app before the countdown ends."));}
        else
        {
            ResetForActionEdit();action.TargetHandle=handle;action.TargetProcessId=pid;action.TargetTitle=NativeInput.GetWindowTitle(handle);
            if(_selected==action)LoadForm();SaveConfiguration();Log(L("已绑定：","Bound: ")+action.TargetTitle);Notify(L("目标已绑定","Target bound"),action.TargetTitle,true);
        }
        BindButton.IsEnabled=true;
    }
    private void CaptureClick(object sender,RoutedEventArgs e)
    {
        if(_recordPoints){FinishCapture(true);return;}
        if(_selected?.Kind!=ActionKind.Mouse||!SaveForm(true))return;
        if(!_hotkeysReady){ShowToast(L("请先在设置中注册录制快捷键。","Register capture shortcuts in Settings first."));return;}
        if(!NativeInput.IsValidTarget(_selected.TargetHandle,_selected.TargetProcessId)){ShowToast(L("请先绑定目标窗口。","Bind a target window first."));return;}
        ResetForActionEdit();_recordPoints=true;CaptureButton.Content=L("✓  完成录制","✓  Finish recording");ShowToast(L("切换到目标窗口，F6 依次记录位置，F7 完成。","Switch to the target. F6 adds points; F7 finishes."));
    }
    private void CapturePoint()
    {
        if(!_recordPoints||_selected is null||_engine.IsRunning)return;
        if(_selected.Points.Count>=100){Notify("KeyTempo",L("最多支持 100 个位置","Maximum 100 points"),true);return;}
        if(!NativeInput.IsForeground(_selected.TargetHandle)){Notify("KeyTempo",L("请切换到绑定窗口后再记录。","Switch to the bound window first."),true);return;}
        var point=NativeInput.CapturePoint(_selected.TargetHandle,_selected.CoordinateMode);if(point is null)return;
        var error=NativeInput.ValidatePoint(_selected.TargetHandle,_selected.CoordinateMode,point);if(error is not null){Notify("KeyTempo",error,true);return;}
        _selected.Points.Add(point);RefreshPoints(_selected.Points.Count-1);SaveConfiguration();UpdateRows();Notify(L("位置已记录","Point recorded"),$"{_selected.Points.Count:00}  {point}",true);Log(L("记录位置：","Captured: ")+point);
    }
    private void FinishCapture(bool show)
    {
        if(!_recordPoints)return;_recordPoints=false;CaptureButton.Content=L("◎  录制位置","◎  Record positions");if(show)Notify(L("录制完成","Capture complete"),L("已保存全部鼠标位置。","All positions saved."),true);
    }
}
