using AutoPilotInput.Core;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button=System.Windows.Controls.Button;
using Color=System.Windows.Media.Color;
using ColorConverter=System.Windows.Media.ColorConverter;
namespace AutoPilotInput;
public partial class MainWindow
{
    private void LoadSettings()
    {
        var old=_loading;_loading=true;ThemeCombo.SelectedIndex=_settings.Theme=="Light"?1:0;LanguageCombo.SelectedIndex=English?1:0;MotionCombo.SelectedIndex=_settings.Effects switch{"PowerSaving"=>0,"Fancy"=>2,_=>1};
        AccentBox.Text=_settings.Accent;OpacitySlider.Value=_settings.Opacity;SoundCheck.IsChecked=_settings.Sound;NotificationsCheck.IsChecked=_settings.Notifications;StartHotkeyBox.Text=_settings.StartHotkey;StopHotkeyBox.Text=_settings.StopHotkey;RepositoryBox.Text=_settings.GitHubRepository;
        TargetPolicyCombo.SelectedIndex=_settings.TargetErrorPolicy switch{"PauseAll"=>1,"Skip" or "SkipCycle"=>2,_=>0};PointPolicyCombo.SelectedIndex=_settings.PointErrorPolicy switch{"PauseAction"=>1,"PauseAll"=>2,_=>0};InputPolicyCombo.SelectedIndex=_settings.InputErrorPolicy switch{"PauseAll"=>1,"Skip" or "SkipCycle"=>2,_=>0};_loading=old;
    }
    private void SettingsClick(object sender,RoutedEventArgs e){if(!SaveForm(true))return;LoadSettings();SettingsOverlay.Visibility=Visibility.Visible;Translate(SettingsOverlay);}
    private void SettingsCloseClick(object sender,RoutedEventArgs e){ApplySettings();SettingsOverlay.Visibility=Visibility.Collapsed;}
    private void SettingsChanged(object sender,SelectionChangedEventArgs e){if(!_loading)ApplySettings();}
    private void SettingsToggled(object sender,RoutedEventArgs e){if(!_loading)ApplySettings();}
    private void SettingsLostFocus(object sender,RoutedEventArgs e){if(!_loading)ApplySettings();}
    private void OpacityChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!_loading){_settings.Opacity=OpacitySlider.Value;Opacity=_settings.Opacity;SaveConfiguration();}}
    private void ApplySettings()
    {
        if(_loading)return;
        if(!Regex.IsMatch(AccentBox.Text,"^#[0-9a-fA-F]{6}$")){ShowToast(L("强调色格式：#RRGGBB","Use a color in #RRGGBB format"));return;}
        if(RepositoryBox.Text.Length>0&&!Regex.IsMatch(RepositoryBox.Text,"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$")){ShowToast(L("仓库格式为 owner/repo","Repository format: owner/repo"));return;}
        var oldLanguage=_settings.Language;
        _settings.Theme=ThemeCombo.SelectedIndex==1?"Light":"Dark";_settings.Language=LanguageCombo.SelectedIndex==1?"en-US":"zh-CN";_settings.Effects=MotionCombo.SelectedIndex switch{0=>"PowerSaving",2=>"Fancy",_=>"Standard"};_settings.Accent=AccentBox.Text;_settings.Sound=SoundCheck.IsChecked==true;_settings.Notifications=NotificationsCheck.IsChecked==true;_settings.GitHubRepository=RepositoryBox.Text;
        _settings.TargetErrorPolicy=TargetPolicyCombo.SelectedIndex switch{1=>"PauseAll",2=>"SkipCycle",_=>"PauseAction"};_settings.InputErrorPolicy=InputPolicyCombo.SelectedIndex switch{1=>"PauseAll",2=>"SkipCycle",_=>"PauseAction"};_settings.PointErrorPolicy=PointPolicyCombo.SelectedIndex switch{1=>"PauseAction",2=>"PauseAll",_=>"SkipCycle"};UpdatePolicies();
        if(oldLanguage!=_settings.Language){FillChoices();LoadSettings();LoadForm();_loading=false;if(_tray is not null){_tray.Dispose();CreateTray();}}
        ApplyAppearance();UpdateStatus();SaveConfiguration();
    }
    private void UpdatePolicies()
    {
        static ErrorPolicy Parse(string s)=>s switch{"PauseAll"=>ErrorPolicy.PauseAll,"Skip" or "SkipCycle"=>ErrorPolicy.SkipCycle,_=>ErrorPolicy.PauseAction};
        _engineSettings.WindowFailure=Parse(_settings.TargetErrorPolicy);_engineSettings.InputFailure=Parse(_settings.InputErrorPolicy);_engineSettings.InvalidPoint=Parse(_settings.PointErrorPolicy);
    }
    private void ApplyAppearance()
    {
        var light=_settings.Theme=="Light";var palette=new Dictionary<string,string>{["Backdrop"]=light?"#F1F2F8":"#10111B",["Surface"]=light?"#FAFFFFFF":"#F0191B28",["Surface2"]=light?"#E9EAF3":"#242634",["Text"]=light?"#23223B":"#F1EFFB",["Muted"]=light?"#656780":"#9B9CB4",["Stroke"]=light?"#D4D5E3":"#373748",["AccentDim"]=light?"#EAE3FC":"#302440",["Accent"]=_settings.Accent};
        foreach(var p in palette)System.Windows.Application.Current.Resources[p.Key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(p.Value));
        Opacity=_settings.Opacity;Translate(this);Glow.BeginAnimation(UIElement.OpacityProperty,null);Glow.Opacity=.35;
        if(_settings.Effects=="Fancy")Glow.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.2,.7,TimeSpan.FromSeconds(3)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
        _uiTimer.Interval=TimeSpan.FromMilliseconds(_settings.Effects=="PowerSaving"?300:100);
    }
    private void Translate(DependencyObject parent)
    {
        if(parent is FrameworkElement f&&f.Tag is string text&&text.Contains('|')){var parts=text.Split('|');var value=parts[English?1:0];if(f is TextBlock t)t.Text=value;else if(f is ContentControl c)c.Content=value;}
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)Translate(VisualTreeHelper.GetChild(parent,i));
    }
    private void ApplyHotkeysClick(object sender,RoutedEventArgs e)
    {
        if(_engine.IsRunning){ShowToast(L("请先停止再修改快捷键。","Stop before changing shortcuts."));return;}if(_hotkeys is null)return;
        if(!_hotkeys.Configure(StartHotkeyBox.Text,StopHotkeyBox.Text,_settings.CaptureHotkey,out var error)){ShowToast(error??"Hotkey error");return;}
        _hotkeysReady=true;_settings.StartHotkey=StartHotkeyBox.Text;_settings.StopHotkey=StopHotkeyBox.Text;SaveConfiguration();UpdateStatus();ShowToast(L("快捷键已更新。","Shortcuts updated."));
    }
    private void ImportClick(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter="KeyTempo configuration (*.json)|*.json"};if(dialog.ShowDialog(this)!=true)return;
        try
        {
            var incoming=SettingsStore.Import(dialog.FileName);
            if(_hotkeys is not null&&!_hotkeys.Configure(incoming.StartHotkey,incoming.StopHotkey,incoming.CaptureHotkey,out var error)){ShowToast(error??"Hotkey error");return;}
            StopAll();_engine.Dispose();_settings=incoming;_hotkeysReady=_hotkeys is not null;foreach(var a in _settings.Actions){a.TargetHandle=0;a.TargetProcessId=0;}
            InitializeEngine();FillChoices();LoadSettings();RenderActions(_settings.Actions.FirstOrDefault()?.Id);_loading=false;ApplyAppearance();SaveConfiguration();ShowToast(L("导入完成。运行前请重新绑定目标窗口。","Imported. Rebind targets before running."));
        }
        catch(Exception ex){ShowToast(ex.Message);}
    }
    private void ExportClick(object sender,RoutedEventArgs e)
    {
        if(!SaveForm(true))return;var dialog=new Microsoft.Win32.SaveFileDialog{Filter="KeyTempo configuration (*.json)|*.json",FileName="keytempo-config.json"};if(dialog.ShowDialog(this)!=true)return;
        try{SettingsStore.Export(dialog.FileName,_settings);ShowToast(L("配置已导出。","Configuration exported."));}catch(Exception ex){ShowToast(ex.Message);}
    }
    private async void UpdateClick(object sender,RoutedEventArgs e)
    {
        ApplySettings();var repo=_settings.GitHubRepository;if(string.IsNullOrWhiteSpace(repo)){ShowToast(L("发布到 GitHub 后，请先填入 owner/repo。","Enter owner/repo after publishing to GitHub."));return;}
        var button=(Button)sender;button.IsEnabled=false;
        try
        {
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};client.DefaultRequestHeaders.UserAgent.ParseAdd("KeyTempo/1.0");using var response=await client.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound){ShowToast(L("仓库尚无公开 Release，或仓库地址不存在。","No public release found, or the repository does not exist."));return;}
            response.EnsureSuccessStatusCode();using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var tag=json.RootElement.GetProperty("tag_name").GetString()??"";
            if(Version.TryParse(tag.TrimStart('v','V'),out var latest)&&latest<=new Version(1,0,0)){ShowToast(L("当前已是最新版本。","You are up to date."));return;}
            if(System.Windows.MessageBox.Show(this,L($"GitHub 最新版本：{tag}。打开下载页面？",$"Latest GitHub release: {tag}. Open the download page?"),"KeyTempo",MessageBoxButton.YesNo,MessageBoxImage.Information)==MessageBoxResult.Yes)Process.Start(new ProcessStartInfo($"https://github.com/{repo}/releases/latest"){UseShellExecute=true});
        }
        catch(Exception ex){ShowToast(L("检查失败：","Update check failed: ")+ex.Message);}finally{button.IsEnabled=true;}
    }
}
