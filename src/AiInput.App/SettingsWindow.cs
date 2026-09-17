using AiInput.Windows;
using AiInput.Core;
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
    readonly TextBlock updateStatus=new(){TextWrapping=TextWrapping.Wrap};
    readonly ProgressBar updateProgress=new(){Minimum=0,Maximum=100};
    readonly Button checkUpdate=new(){Content="检查更新"};
    readonly Button installUpdate=new(){Content="下载并安装"};
    readonly Button cancelUpdate=new(){Content="取消下载"};
    public SettingsWindow(Controller controller)
    {
        this.controller=controller;Title="AI 输入助手 · "+ProductInfo.VersionText;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","App.ico"));
        AppWindow.Closing+=(_,args)=>
        {
            if(!controller.IsQuitting)
            {
                args.Cancel=true;
                DispatcherQueue.TryEnqueue(()=>{if(!controller.IsQuitting){AppWindow.IsShownInSwitchers=false;AppWindow.Hide();}});
            }
        };
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(700,780));
        var panel=new StackPanel{Spacing=18,Margin=new Thickness(32),MaxWidth=640,HorizontalAlignment=HorizontalAlignment.Stretch};
        panel.Children.Add(new TextBlock{Text="AI 输入助手",FontSize=30,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="从光标接着写，让一段话完整收尾。",FontSize=16,Foreground=new SolidColorBrush(Microsoft.UI.Colors.Gray)});
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
        panel.Children.Add(new TextBlock{Text="思考深度",FontSize=18});
        var depth=new ComboBox{HorizontalAlignment=HorizontalAlignment.Stretch};
        foreach(var option in new[]{("auto","自动（默认）"),("max","Max 深度"),("none","不思考")})
        {
            var item=new ComboBoxItem{Content=option.Item2,Tag=option.Item1};depth.Items.Add(item);
            if(option.Item1==controller.Settings.ThinkingDepth)depth.SelectedItem=item;
        }
        if(depth.SelectedItem==null)depth.SelectedIndex=0;
        depth.SelectionChanged+=(_,_)=>
        {
            try
            {
                controller.Settings.ThinkingDepth=(string)((ComboBoxItem)depth.SelectedItem).Tag;
                controller.SaveSettings();feedback.Text="思考深度已保存，请重新启用。";
            }
            catch(Exception e){LocalStore.Log("ThinkingSettingFailed",e);feedback.Text="思考深度保存失败";}
        };
        panel.Children.Add(depth);
        panel.Children.Add(new TextBlock{Text="自动采用服务默认思考策略；Max 使用最高思考强度，通常耗时更长、用量更多；不思考直接生成正文。仅显示最终续写。",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="浮窗外观",FontSize=18});
        var font=new Slider{Minimum=12,Maximum=32,StepFrequency=1,Value=controller.Settings.FontSize,Header="统一字号（状态、正文与提示）"};
        var alpha=new Slider{Minimum=25,Maximum=100,StepFrequency=5,Value=controller.Settings.Opacity*100,Header="背景不透明度（%）"};
        panel.Children.Add(font);panel.Children.Add(alpha);
        panel.Children.Add(new TextBlock{Text="快捷键（均为 Ctrl + Alt + 下列按键）",FontSize=18});
        var toggleKey=KeyBox("启用／暂停",controller.Settings.ToggleKey);
        var screenKey=KeyBox("截取当前输入窗口所在显示器",controller.Settings.ScreenshotKey);
        var acceptKey=KeyBox("采纳建议／复制选区续写",controller.Settings.AcceptKey);
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
        panel.Children.Add(new TextBlock{Text="停顿 1.5 秒后生成；无选区时读取光标前后各 300 字符，采纳后原位插入。有选区时只根据选中文字续写，采纳键复制结果到剪贴板，保留原文；不附带截图。选区最多 8192 个 UTF-16 单元，超限会提示缩小选区。每条建议由你确认。启动和解锁后默认暂停；暂停时隐藏浮窗。启用后可使用截图快捷键。\n截图快捷键会上传该显示器当前可见画面，仅用于一次生成；正文和截图不写入日志。部分应用无法提供可靠的光标或中文输入法状态，将显示暂不支持。",TextWrapping=TextWrapping.Wrap});
        var export=new Button{Content="导出诊断日志"};export.Click+=(_,_)=>{
            using var dialog=new Forms.SaveFileDialog{Filter="ZIP 文件|*.zip",FileName="AiInput-log-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip",OverwritePrompt=false};
            if(dialog.ShowDialog()==Forms.DialogResult.OK)
            {
                try {if(File.Exists(dialog.FileName)){feedback.Text="请选用新的文件名。";return;}LocalStore.Export(dialog.FileName,controller.Settings);feedback.Text="诊断日志已导出，不包含正文或密钥。";}
                catch(Exception e){LocalStore.Log("ExportFailed",e);feedback.Text="导出失败";}
            }
        };panel.Children.Add(export);panel.Children.Add(feedback);
        panel.Children.Add(new TextBlock{Text="软件更新 · 当前版本 "+ProductInfo.VersionText,FontSize=18});
        var automatic=new ToggleSwitch{Header="自动检查并下载新版本",IsOn=controller.Settings.AutoUpdate};
        automatic.Toggled+=(_,_)=>controller.Updates.SetAutomatic(automatic.IsOn);
        panel.Children.Add(automatic);panel.Children.Add(updateStatus);panel.Children.Add(updateProgress);
        var updateButtons=new StackPanel{Orientation=Orientation.Horizontal,Spacing=12};
        checkUpdate.Click+=async(_,_)=>await controller.Updates.CheckAsync();
        installUpdate.Click+=async(_,_)=>await controller.Updates.InstallAsync();
        cancelUpdate.Click+=(_,_)=>controller.Updates.Cancel();
        updateButtons.Children.Add(checkUpdate);updateButtons.Children.Add(installUpdate);updateButtons.Children.Add(cancelUpdate);panel.Children.Add(updateButtons);
        panel.Children.Add(new HyperlinkButton{Content="在 GitHub 查看版本与下载",NavigateUri=new Uri(ProductInfo.ReleasesUrl)});
        panel.Children.Add(new TextBlock{Text="下载完成后点击“重启并安装”，程序会保存设置、退出并完成安装，然后重新启动。"+(controller.Updates.Portable?" 当前为便携版；更新后会在当前目录生成卸载程序。":""),TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock{Text="关闭此窗口会隐藏到后台，不占用任务栏或 Alt+Tab 列表。双击托盘图标或状态胶囊可打开设置；完全退出请使用右键菜单。",TextWrapping=TextWrapping.Wrap});
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        controller.Changed+=Update;controller.Updates.Changed+=UpdateDownload;
        Closed+=(_,_)=>{controller.Changed-=Update;controller.Updates.Changed-=UpdateDownload;};Update();UpdateDownload();
    }
    void Update()=>status.Text=controller.Status+(controller.HotkeyError.Length>0?"\n快捷键问题："+controller.HotkeyError:"");
    public void ShowUpdates()=>DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>updateStatus.StartBringIntoView());
    void UpdateDownload()
    {
        var u=controller.Updates;
        updateStatus.Text=u.Message;updateProgress.Value=u.Progress;
        updateProgress.Visibility=u.Downloading?Visibility.Visible:Visibility.Collapsed;
        checkUpdate.IsEnabled=!u.Busy;installUpdate.IsEnabled=!u.Busy&&u.Available!=null;
        installUpdate.Content=u.Ready?"重启并安装":"下载并安装";
        cancelUpdate.Visibility=u.Busy?Visibility.Visible:Visibility.Collapsed;
        cancelUpdate.Content=u.Downloading?"取消下载":"取消";
    }
    static ComboBox KeyBox(string label,uint selected)
    {
        var box=new ComboBox{Header=label,HorizontalAlignment=HorizontalAlignment.Stretch};
        uint[] keys=[0x20,0x0D,0x53,0x41,0x44,0x46,0x47,0x51,0x57,0x58,0x5A,0x70,0x71,0x72,0x73,0x74,0x75,0x76,0x77];
        foreach(uint code in keys){var item=new ComboBoxItem{Content=SuggestionWindow.KeyName(code),Tag=code};box.Items.Add(item);if(code==selected)box.SelectedItem=item;}
        if(box.SelectedItem==null)box.SelectedIndex=0;
        return box;
    }
}
