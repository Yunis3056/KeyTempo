using AutoPilotInput.Core;
using System.Runtime.InteropServices;
class Program
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [STAThread] static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var form=new Form { Text="AutoPilot · isolated input verification",Width=540,Height=240,StartPosition=FormStartPosition.CenterScreen,TopMost=true };
        var text=new TextBox {Left=24,Top=20,Width=460,ImeMode=ImeMode.Disable};var button=new Button {Left=24,Top=70,Width=200,Height=65,Text="Input test receiver"};form.Controls.Add(text);form.Controls.Add(button);int clicks=0;button.Click+=(_,_)=>clicks++;
        form.Shown+=async(_,_)=>
        {
            try
            {
                SetForegroundWindow(form.Handle);text.Focus();await Task.Delay(200);
                if(!NativeInput.IsForeground(form.Handle.ToInt64()))throw new Exception("Test target did not acquire focus; no input sent.");
                await NativeInput.SendKeyboardAsync([65],CancellationToken.None);await Task.Delay(100);
                if(text.Text!="a"&&text.Text!="A")throw new Exception("Keyboard input did not reach test receiver: "+text.Text);
                await NativeInput.SendKeyboardAsync([17,65],CancellationToken.None);await Task.Delay(100);
                if(text.SelectionLength!=1)throw new Exception("Ctrl+A combo was not received.");
                var point=new ClickPoint{X=80,Y=100};var h=form.Handle.ToInt64();
                await NativeInput.SendMouseAsync(h,CoordinateMode.Window,point,AutoPilotInput.Core.MouseButton.Left,false,CancellationToken.None);await Task.Delay(100);
                if(clicks!=1)throw new Exception("Mouse click was not received.");
                form.Location=new System.Drawing.Point(form.Left+30,form.Top+15);await Task.Delay(100);
                await NativeInput.SendMouseAsync(h,CoordinateMode.Window,point,AutoPilotInput.Core.MouseButton.Left,false,CancellationToken.None);await Task.Delay(100);
                if(clicks!=2)throw new Exception("Window relative coordinate did not follow moved target.");
                Console.WriteLine("PASS: real Windows keyboard input, Ctrl+A, mouse click, and window-relative click after moving target.");Environment.ExitCode=0;
            }
            catch(Exception error){Console.WriteLine("FAIL: "+error.Message);Environment.ExitCode=1;}
            finally{form.Close();}
        };
        Application.Run(form);
    }
}
