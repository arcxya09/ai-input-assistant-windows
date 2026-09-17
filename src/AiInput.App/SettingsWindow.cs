using AiInput.Windows;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Forms=System.Windows.Forms;
namespace AiInput.App;

public sealed class SettingsWindow : Window
{
    readonly Controller controller;
    readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    readonly TextBlock feedback=new(){TextWrapping=TextWrapping.Wrap};
    readonly PasswordBox key=new(){PlaceholderText="填写 DeepSeek 官方 API Key",MaxWidth=600,HorizontalAlignment=HorizontalAlignment.Stretch};
    public SettingsWindow(Controller controller)
    {
        this.controller=controller;Title="AI 输入助手 · 1.0";
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(700,780));
        var panel=new StackPanel{Spacing=18,Margin=new Thickness(32),MaxWidth=640,HorizontalAlignment=HorizontalAlignment.Stretch};
        panel.Children.Add(new TextBlock{Text="AI 输入助手",FontSize=30,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="在你停顿时，接着写一句。",FontSize=16,Foreground=new SolidColorBrush(Microsoft.UI.Colors.Gray)});
        panel.Children.Add(status);
        var toggle=new Button{Content="启用／暂停"};toggle.Click+=(_,_)=>controller.Toggle();panel.Children.Add(toggle);
        panel.Children.Add(new TextBlock{Text="DeepSeek API Key",FontSize=18});
        panel.Children.Add(key);
        panel.Children.Add(new TextBlock{Text=LocalStore.ReadKey().Length>0?"已保存密钥。留空保存将保留现有密钥。":"只需填写 API Key，接口和模型由软件管理。",TextWrapping=TextWrapping.Wrap});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};
        var save=new Button{Content="保存密钥"};save.Click+=(_,_)=>{
            try{if(!string.IsNullOrWhiteSpace(key.Password))LocalStore.SaveKey(key.Password);key.Password="";controller.Pause();feedback.Text="密钥已加密保存。请回到输入窗口后使用快捷键启用。";}
            catch(Exception e){LocalStore.Log("KeySaveFailed",e);feedback.Text="保存失败，请导出诊断日志。";}
        };
        var clear=new Button{Content="删除密钥"};clear.Click+=(_,_)=>{try{LocalStore.DeleteKey();key.Password="";controller.Pause();feedback.Text="密钥已删除";}catch(Exception e){LocalStore.Log("KeyDeleteFailed",e);}};
        buttons.Children.Add(save);buttons.Children.Add(clear);panel.Children.Add(buttons);
        panel.Children.Add(new TextBlock{Text="浮窗外观",FontSize=18});
        var font=new Slider{Minimum=12,Maximum=32,StepFrequency=1,Value=controller.Settings.FontSize,Header="字号"};
        var alpha=new Slider{Minimum=25,Maximum=100,StepFrequency=5,Value=controller.Settings.Opacity*100,Header="背景不透明度（%）"};
        panel.Children.Add(font);panel.Children.Add(alpha);
        panel.Children.Add(new TextBlock{Text="快捷键（均为 Ctrl + Alt + 下列按键）",FontSize=18});
        var toggleKey=KeyBox("启用／暂停",controller.Settings.ToggleKey);
        var screenKey=KeyBox("截取当前输入窗口所在显示器",controller.Settings.ScreenshotKey);
        var acceptKey=KeyBox("采纳当前建议",controller.Settings.AcceptKey);
        panel.Children.Add(toggleKey);panel.Children.Add(screenKey);panel.Children.Add(acceptKey);
        var appearance=new Button{Content="保存外观与快捷键"};
        appearance.Click+=(_,_)=>{
            try
            {
                uint a=(uint)((ComboBoxItem)toggleKey.SelectedItem).Tag;
                uint b=(uint)((ComboBoxItem)screenKey.SelectedItem).Tag;
                uint c=(uint)((ComboBoxItem)acceptKey.SelectedItem).Tag;
                if(a==b||a==c||b==c){feedback.Text="三个快捷键需要不同。";return;}
                controller.Settings.FontSize=font.Value;controller.Settings.Opacity=alpha.Value/100;
                controller.Settings.ToggleKey=a;controller.Settings.ScreenshotKey=b;controller.Settings.AcceptKey=c;
                controller.SaveSettings();feedback.Text=controller.HotkeyError.Length==0?"已保存":"快捷键冲突："+controller.HotkeyError;
            }
            catch(Exception e){LocalStore.Log("SettingsSaveFailed",e);feedback.Text="设置保存失败";}
        };panel.Children.Add(appearance);
        panel.Children.Add(new TextBlock{Text="停顿 1.5 秒后生成；前后文各 300 字符；每条建议由你确认。启动和解锁后默认暂停。\n截图快捷键会上传该显示器当前可见画面，仅用于一次生成；正文和截图不写入日志。部分应用无法提供可靠的光标或中文输入法状态，将显示暂不支持。",TextWrapping=TextWrapping.Wrap});
        var export=new Button{Content="导出诊断日志"};export.Click+=(_,_)=>{
            using var dialog=new Forms.SaveFileDialog{Filter="ZIP 文件|*.zip",FileName="AiInput-log-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip",OverwritePrompt=false};
            if(dialog.ShowDialog()==Forms.DialogResult.OK)
            {
                try {if(File.Exists(dialog.FileName)){feedback.Text="请选用新的文件名。";return;}LocalStore.Export(dialog.FileName,controller.Settings);feedback.Text="诊断日志已导出，不包含正文或密钥。";}
                catch(Exception e){LocalStore.Log("ExportFailed",e);feedback.Text="导出失败";}
            }
        };panel.Children.Add(export);panel.Children.Add(feedback);
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        controller.Changed+=Update;Closed+=(_,_)=>controller.Changed-=Update;Update();
    }
    void Update()=>status.Text=controller.Status+(controller.HotkeyError.Length>0?"\n快捷键问题："+controller.HotkeyError:"");
    static ComboBox KeyBox(string label,uint selected)
    {
        var box=new ComboBox{Header=label,HorizontalAlignment=HorizontalAlignment.Stretch};
        uint[] keys=[0x20,0x0D,0x53,0x41,0x44,0x46,0x47,0x51,0x57,0x58,0x5A,0x70,0x71,0x72,0x73,0x74,0x75,0x76,0x77];
        foreach(uint code in keys){var item=new ComboBoxItem{Content=SuggestionWindow.KeyName(code),Tag=code};box.Items.Add(item);if(code==selected)box.SelectedItem=item;}
        if(box.SelectedItem==null)box.SelectedIndex=0;
        return box;
    }
}
