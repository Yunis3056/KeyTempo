using System.IO;
using System.Windows;
using AutoPilotInput.Core;
namespace AutoPilotInput;
public partial class MainWindow
{
    // Explicit preview mode only: exercises real UI event bindings without registering hotkeys or sending input.
    private void VerifyInterface()
    {
        var passed=new List<string>();
        void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);passed.Add(message);}
        Check(_selected!.Keys.Count==0,"No default key");
        KeyCombo.SelectedItem=((IEnumerable<KeyOption>)KeyCombo.ItemsSource).First(k=>k.Key==65);
        Check(SaveForm(true)&&_selected.Keys.SequenceEqual(new[]{65}),"Key selection saves through UI binding");
        IntervalBox.Text="NaN";Check(!SaveForm(false)&&_selected.IntervalSeconds==300,"Invalid interval cannot replace valid config");
        IntervalBox.Text="5";Check(SaveForm(true)&&_selected.IntervalSeconds==5,"Edited interval saves");
        IntervalModeCombo.SelectedIndex=1;IntervalBox.Text="2";SecondIntervalBox.Text="4";
        Check(SaveForm(true)&&_selected.IntervalMode==IntervalMode.Range&&_selected.MinSeconds==2&&_selected.MaxSeconds==4,"Random range editor saves");
        FirstRunCombo.SelectedIndex=1;Check(SaveForm(true)&&_selected.RunImmediately,"Immediate mode saves");
        AddMouseClick(this,new RoutedEventArgs());Check(_settings.Actions.Count==2&&_selected.Kind==ActionKind.Mouse,"Mouse action can be added");
        _selected.Points.Add(new ClickPoint{X=50,Y=60});_selected.Points.Add(new ClickPoint{X=100,Y=120});RefreshPoints(1);MovePointUpClick(this,new RoutedEventArgs());
        Check(_selected.Points[0].X==100,"Mouse positions can be reordered");
        CoordinateCombo.SelectedIndex=1;Check(CoordinateCombo.SelectedIndex==0,"Existing coordinates cannot be silently reinterpreted");
        ThemeCombo.SelectedIndex=1;Check(_settings.Theme=="Light","Theme switch applies");
        LanguageCombo.SelectedIndex=1;Check(_settings.Language=="en-US"&&StartButton.Content.ToString()!.Contains("Start"),"English UI switch applies");
        LanguageCombo.SelectedIndex=0;ThemeCombo.SelectedIndex=App.PreviewLight?1:0;
        _settings.Actions.Clear();_settings.Actions.Add(NewAction(ActionKind.Keyboard));RenderActions(_settings.Actions[0].Id);UpdateStatus();
        Toast.Visibility=Visibility.Collapsed;File.WriteAllLines(Path.ChangeExtension(App.PreviewPath!,"checks.txt"),passed.Select(x=>"PASS "+x));
    }
    private void PreparePreviewScenario()
    {
        if(App.PreviewScenario=="settings"){SettingsOverlay.Visibility=Visibility.Visible;Translate(SettingsOverlay);}
        if(App.PreviewScenario=="mouse"){AddMouseClick(this,new RoutedEventArgs());_selected!.Points.Add(new ClickPoint{X=320,Y=210});_selected.Points.Add(new ClickPoint{X=480,Y=330});RefreshPoints();UpdateRows();}
        if(App.PreviewScenario=="english"){LanguageCombo.SelectedIndex=1;}
        if(App.PreviewScenario=="small"){Width=1000;Height=680;}
    }
}
