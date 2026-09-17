using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
namespace AiInput.Windows;

public sealed class SuggestionWindow : Form
{
    readonly Settings settings;
    readonly ToolStripMenuItem statusItem=new(){Enabled=false};
    string text="",status="已暂停";
    bool enabled,working,resizing,copyMode;
    float scroll,contentHeight;
    public event Action? BoundsSaved;
    public event Action? OpenSettingsRequested;
    public event Action? ToggleRequested;
    public event Action? ExitRequested;
    public SuggestionWindow(Settings settings)
    {
        this.settings=settings;
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        StartPosition=FormStartPosition.Manual;
        MinimumSize=new Size(88,30);
        Bounds=new Rectangle(settings.X,settings.Y,120,30);
        EnsureVisible();
        ResizeEnd+=(_,_)=>SaveBounds();
        var menu=new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("上一页建议",null,(_,_)=>ScrollBy(-BodyHeight));
        menu.Items.Add("下一页建议",null,(_,_)=>ScrollBy(BodyHeight));
        menu.Items.Add("启用／暂停",null,(_,_)=>ToggleRequested?.Invoke());
        menu.Items.Add("打开设置",null,(_,_)=>OpenSettingsRequested?.Invoke());
        menu.Items.Add("退出",null,(_,_)=>ExitRequested?.Invoke());
        ContextMenuStrip=menu;
    }
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams
    {
        get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x00080000|0x80;return p;}
    }
    float DpiScale=>IsHandleCreated?Math.Max(1,Native.GetDpiForWindow(Handle)/96f):1;
    float BodyHeight=>Math.Max(1,Height-76*DpiScale);
    string CompactStatus
    {
        get
        {
            if(status.StartsWith("已暂停")||status.Contains("当前暂停"))return "已暂停";
            if(status.StartsWith("已启用"))return "等待输入";
            if(status.StartsWith("正在识别"))return "识别输入框…";
            if(status.StartsWith("已选中"))return status.Replace(" · 正在续写…"," · 续写中…");
            if(status.StartsWith("选区续写"))return "选区续写就绪";
            if(status.StartsWith("已复制"))return "已复制";
            if(status.StartsWith("上下文不足"))return "上下文不足";
            if(status.StartsWith("已读取文字，但模型"))return "模型未能续写";
            if(status.StartsWith("未能稳定读取"))return "光标或选区未就绪";
            if(status.StartsWith("已识别空输入框"))return "等待文字";
            if(status.StartsWith("本次无法续写"))return "暂无法续写";
            if(status.StartsWith("本次没有生成"))return "暂无续写";
            if(status.StartsWith("续写返回异常"))return "续写返回异常";
            if(status.StartsWith("已识别"))return status.Contains("截图")?"截图续写中…":status.Replace(" · 正在续写…"," · 续写中…");
            if(status.StartsWith("建议已就绪"))return "续写已就绪";
            if(status.Contains("候选词"))return "等待选词";
            if(status.Contains("未完整结束"))return "续写未完成";
            if(status.Contains("API Key"))return "请设置 API Key";
            if(status.Contains("没有合适"))return "暂无合适续写";
            if(status.Contains("请将光标"))return "等待输入框";
            return status;
        }
    }
    public void Present(string value,bool copy=false)
    {
        text=value;scroll=0;copyMode=copy;
        SetDisplaySize();EnsureVisible();Render();Show();
        Native.SetWindowPos(Handle,-1,Left,Top,Width,Height,0x0010|0x0040);
    }
    public void Conceal(){text="";scroll=0;copyMode=false;SetDisplaySize();EnsureVisible();if(Visible)Render();}
    public void SetStatus(string value,bool active,bool busy)
    {
        status=value;statusItem.Text=value;enabled=active;working=busy&&value.Contains("正在");
        SetDisplaySize();EnsureVisible();Render();
        if(!Visible)Show();
        Native.SetWindowPos(Handle,-1,Left,Top,Width,Height,0x0010|0x0040);
    }
    void SetDisplaySize()
    {
        if(resizing)return;
        resizing=true;
        try
        {
            float scale=DpiScale;
            MinimumSize=text.Length==0?new Size((int)(88*scale),(int)(30*scale)):new Size((int)(280*scale),(int)(120*scale));
            using var bitmap=new Bitmap(1,1);
            using var g=Graphics.FromImage(bitmap);
            if(text.Length==0)
            {
                using var label=new Font("Microsoft YaHei UI",12*scale,FontStyle.Regular,GraphicsUnit.Pixel);
                int width=(int)Math.Ceiling(g.MeasureString(CompactStatus,label).Width+38*scale);
                Size=new Size(Math.Clamp(width,(int)(88*scale),(int)(240*scale)),(int)(30*scale));
            }
            else
            {
                var area=Screen.FromRectangle(Bounds).WorkingArea;
                int width=Math.Clamp(settings.Width,(int)(320*scale),Math.Max((int)(320*scale),Math.Min((int)(900*scale),area.Width)));
                using var font=new Font("Microsoft YaHei UI",(float)settings.FontSize*scale,FontStyle.Regular,GraphicsUnit.Pixel);
                using var format=TextFormat();
                float measured=g.MeasureString(text,font,new SizeF(Math.Max(1,width-36*scale),1000000),format).Height+font.GetHeight(g);
                int height=(int)Math.Ceiling(measured+76*scale);
                Size=new Size(width,Math.Clamp(height,(int)(120*scale),Math.Max((int)(120*scale),(int)(area.Height*.65))));
            }
        }
        finally{resizing=false;}
    }
    static StringFormat TextFormat()=>new(StringFormat.GenericTypographic){FormatFlags=StringFormatFlags.MeasureTrailingSpaces};
    public void RefreshStyle(){SetDisplaySize();EnsureVisible();if(Visible)Render();}
    void EnsureVisible()
    {
        var area=Screen.FromRectangle(Bounds).WorkingArea;
        Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));
        Location=new Point(Math.Clamp(Left,area.Left,area.Right-Width),Math.Clamp(Top,area.Top,area.Bottom-Height));
    }
    void SaveBounds()
    {
        settings.X=Left;settings.Y=Top;
        if(text.Length>0){settings.Width=Width;settings.Height=Height;}
        settings.Monitor=Screen.FromRectangle(Bounds).DeviceName;BoundsSaved?.Invoke();
    }
    void ScrollBy(float delta)
    {
        if(text.Length==0)return;
        scroll=Math.Clamp(scroll+delta,0,Math.Max(0,contentHeight-BodyHeight));Render();
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21){m.Result=3;return;}
        if(m.Msg==0xA3){OpenSettingsRequested?.Invoke();m.Result=0;return;}
        if(m.Msg==0xA5){ContextMenuStrip?.Show(Cursor.Position);m.Result=0;return;}
        if(m.Msg==0x20A&&text.Length>0)
        {
            int delta=(short)((m.WParam.ToInt64()>>16)&0xFFFF);
            ScrollBy(-delta/120f*(float)settings.FontSize*DpiScale*3);m.Result=0;return;
        }
        if(m.Msg==0x84)
        {
            long packed=m.LParam.ToInt64();
            var point=PointToClient(new Point((short)(packed&0xFFFF),(short)((packed>>16)&0xFFFF)));
            m.Result=text.Length>0&&point.X>Width-18*DpiScale&&point.Y>Height-18*DpiScale?17:2;return;
        }
        if(m.Msg==0x0232){SaveBounds();Render();}
        if(m.Msg==0x02E0){base.WndProc(ref m);SetDisplaySize();EnsureVisible();Render();return;}
        base.WndProc(ref m);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);if(IsHandleCreated&&Visible)Render();}
    void Render()
    {
        if(Width<1||Height<1)return;
        using var bitmap=new Bitmap(Width,Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(bitmap))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            using var path=new GraphicsPath();int r=text.Length==0?Height-1:24;
            path.AddArc(0,0,r,r,180,90);path.AddArc(Width-r-1,0,r,r,270,90);
            path.AddArc(Width-r-1,Height-r-1,r,r,0,90);path.AddArc(0,Height-r-1,r,r,90,90);path.CloseFigure();
            using var bg=new SolidBrush(Color.FromArgb((int)(255*settings.Opacity),23,29,40));g.FillPath(bg,path);
            using var border=new Pen(Color.FromArgb(180,81,101,126));g.DrawPath(border,path);
            float scale=DpiScale;
            using var font=new Font("Microsoft YaHei UI",(float)settings.FontSize*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            using var small=new Font("Microsoft YaHei UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            using var white=new SolidBrush(Color.White);
            using var muted=new SolidBrush(Color.FromArgb(255,170,187,206));
            using var stateColor=new SolidBrush(working?Color.FromArgb(112,224,210):enabled?Color.FromArgb(119,192,255):Color.FromArgb(150,163,184));
            if(text.Length==0)
            {
                g.FillEllipse(stateColor,11*scale,Height/2f-3*scale,6*scale,6*scale);
                using var label=new Font("Microsoft YaHei UI",12*scale,FontStyle.Regular,GraphicsUnit.Pixel);
                using var format=new StringFormat{LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
                g.DrawString(CompactStatus,label,white,new RectangleF(25*scale,0,Width-35*scale,Height),format);
            }
            else
            {
                using var format=TextFormat();
                contentHeight=g.MeasureString(text,font,new SizeF(Math.Max(1,Width-36*scale),1000000),format).Height+font.GetHeight(g);
                scroll=Math.Clamp(scroll,0,Math.Max(0,contentHeight-BodyHeight));
                var state=g.Save();
                g.SetClip(new RectangleF(18*scale,16*scale,Width-36*scale,BodyHeight));
                g.DrawString(text,font,white,new RectangleF(18*scale,16*scale-scroll,Width-36*scale,contentHeight),format);
                g.Restore(state);
                string paging=contentHeight>BodyHeight?$"滚轮 / 右键翻页 · {Math.Min(100,(int)Math.Ceiling((scroll+BodyHeight)/contentHeight*100))}%":"双击打开设置 · 右键菜单";
                g.DrawString("Ctrl+Alt+"+KeyName(settings.AcceptKey)+(copyMode?" 复制续写到剪贴板":" 采纳完整内容"),small,muted,new RectangleF(18*scale,Height-48*scale,Width-36*scale,20*scale));
                g.DrawString(paging,small,muted,new RectangleF(18*scale,Height-28*scale,Width-36*scale,20*scale));
                g.DrawLine(Pens.SlateGray,Width-15,Height-7,Width-7,Height-15);
            }
        }
        nint screen=Native.GetDC(0),dc=Native.CreateCompatibleDC(screen),hbitmap=bitmap.GetHbitmap(Color.FromArgb(0)),old=Native.SelectObject(dc,hbitmap);
        try
        {
            var pos=new Native.POINT(Left,Top);var source=new Native.POINT(0,0);
            var size=new Native.SIZE(Width,Height);var blend=new Native.BLEND{Op=0,Alpha=255,Format=1};
            Native.UpdateLayeredWindow(Handle,screen,ref pos,ref size,dc,ref source,0,ref blend,2);
        }
        finally{Native.SelectObject(dc,old);Native.DeleteObject(hbitmap);Native.DeleteDC(dc);Native.ReleaseDC(0,screen);}
    }
    // Runs inside the Windows UI smoke harness, using the actual layered window.
    public void VerifyDisplay()
    {
        string previousStatus=status,previousText=text;
        bool previousEnabled=enabled,previousWorking=working;
        nint foreground=Native.GetForegroundWindow();
        try
        {
            Conceal();SetStatus("已暂停",false,false);int compact=Width;
            if(Height>31*DpiScale||Width>120*DpiScale)throw new InvalidOperationException("CapsuleNotCompact");
            SetStatus("已识别 300 字 · 正在续写…",true,true);
            if(Width<=compact||Width>240*DpiScale)throw new InvalidOperationException("CapsuleNotAdaptive");
            string paragraph=string.Concat(Enumerable.Repeat("这里是一段完整的测试内容，用于检查长建议能够完整预览和翻页。",100));
            Present(paragraph);
            if(contentHeight<=BodyHeight)throw new InvalidOperationException("LongPreviewMissing");
            ScrollBy(contentHeight);
            if(scroll<=0||Math.Abs(scroll-(contentHeight-BodyHeight))>1||text!=paragraph)throw new InvalidOperationException("PreviewScrollFailed");
            if(Native.GetForegroundWindow()!=foreground)throw new InvalidOperationException("CapsuleStoleFocus");
            Conceal();SetStatus("已暂停",false,false);
            if(Width!=compact)throw new InvalidOperationException("CapsuleDidNotShrink");
        }
        finally
        {
            Conceal();SetStatus(previousStatus,previousEnabled,previousWorking);
            if(previousText.Length>0)Present(previousText);
        }
    }
    public static string KeyName(uint key)=>key switch{0x20=>"Space",0x0D=>"Enter",_=>((Keys)key).ToString()};
}
