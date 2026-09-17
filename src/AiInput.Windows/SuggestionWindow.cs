using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
namespace AiInput.Windows;

public sealed class SuggestionWindow : Form
{
    readonly Settings settings;
    string text="";
    string status="已暂停";
    bool enabled,working,resizing;
    public event Action? BoundsSaved;
    public event Action? OpenSettingsRequested;
    public event Action? ToggleRequested;
    public event Action? ExitRequested;
    public SuggestionWindow(Settings settings)
    {
        this.settings=settings;
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;
        StartPosition=FormStartPosition.Manual;
        MinimumSize=new Size(240,44);
        MaximumSize=new Size(1200,800);
        Bounds=new Rectangle(settings.X,settings.Y,settings.Width,settings.Height);
        EnsureVisible();
        ResizeEnd+=(_,_)=>SaveBounds();
        var menu=new ContextMenuStrip();
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
    public void Present(string value)
    {
        text=value;
        SetDisplaySize();
        EnsureVisible();
        Render();
        Show();
        Native.SetWindowPos(Handle,-1,Left,Top,Width,Height,0x0010|0x0040);
    }
    public void Conceal(){text="";SetDisplaySize();if(Visible)Render();}
    public void SetStatus(string value,bool active,bool busy)
    {
        status=value;enabled=active;working=busy&&value.Contains("正在");
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
            float scale=IsHandleCreated?Math.Max(1,Native.GetDpiForWindow(Handle)/96f):1;
            MinimumSize=text.Length==0?new Size(240,44):new Size(260,110);
            Size=text.Length==0?new Size((int)(300*scale),(int)(46*scale)):new Size(settings.Width,settings.Height);
        }
        finally{resizing=false;}
    }
    public void RefreshStyle(){SetDisplaySize();if(Visible)Render();}
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
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x21){m.Result=3;return;}
        if(m.Msg==0xA3){OpenSettingsRequested?.Invoke();m.Result=0;return;}
        if(m.Msg==0xA5){ContextMenuStrip?.Show(Cursor.Position);m.Result=0;return;}
        if(m.Msg==0x84)
        {
            long packed=m.LParam.ToInt64();
            var point=PointToClient(new Point((short)(packed&0xFFFF),(short)((packed>>16)&0xFFFF)));
            m.Result=text.Length>0&&point.X>Width-18&&point.Y>Height-18?17:2;return;
        }
        if(m.Msg==0x0232){SaveBounds();Render();}
        if(m.Msg==0x02E0){base.WndProc(ref m);EnsureVisible();Render();return;}
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
            float scale=Native.GetDpiForWindow(Handle)/96f;if(scale<=0)scale=1;
            using var font=new Font("Microsoft YaHei UI",(float)settings.FontSize*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            using var small=new Font("Microsoft YaHei UI",11*scale,FontStyle.Regular,GraphicsUnit.Pixel);
            using var white=new SolidBrush(Color.White);
            using var muted=new SolidBrush(Color.FromArgb(255,170,187,206));
            using var stateColor=new SolidBrush(working?Color.FromArgb(112,224,210):enabled?Color.FromArgb(119,192,255):Color.FromArgb(150,163,184));
            if(text.Length==0)
            {
                g.FillEllipse(stateColor,17*scale,Height/2f-5*scale,10*scale,10*scale);
                using var label=new Font("Microsoft YaHei UI",13*scale,FontStyle.Regular,GraphicsUnit.Pixel);
                using var format=new StringFormat{LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
                g.DrawString(status,label,white,new RectangleF(38*scale,0,Width-52*scale,Height),format);
            }
            else
            {
                g.DrawString(text,font,white,new RectangleF(18,16,Width-36,Height-55));
                g.DrawString("Ctrl+Alt+"+KeyName(settings.AcceptKey)+" 采纳   ·   双击打开设置",small,muted,new RectangleF(18,Height-30,Width-30,24));
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
    public static string KeyName(uint key)=>key switch{0x20=>"Space",0x0D=>"Enter",_=>((Keys)key).ToString()};
}
