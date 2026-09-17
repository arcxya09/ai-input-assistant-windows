using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
namespace AiInput.Windows;
public static class ScreenCapture
{
    public static byte[] Capture(nint window)
    {
        if(!Native.InteractiveDesktop()||Native.GetForegroundWindow()!=window)throw new InvalidOperationException("TargetChanged");
        var bounds=Screen.FromHandle(window).Bounds;
        if(bounds.Width<=0||bounds.Height<=0||bounds.Width>16384||bounds.Height>16384)throw new InvalidOperationException("ScreenSize");
        using var bitmap=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(bitmap))graphics.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
        int nonblack=0;
        for(int y=0;y<bitmap.Height;y+=Math.Max(1,bitmap.Height/20))
            for(int x=0;x<bitmap.Width;x+=Math.Max(1,bitmap.Width/20))
                if(bitmap.GetPixel(x,y).ToArgb()!=Color.Black.ToArgb())nonblack++;
        if(nonblack==0)throw new InvalidOperationException("CaptureBlack");
        for(int edge=2560;edge>=1280;edge-=320)
        {
            double ratio=Math.Min(1,(double)edge/Math.Max(bitmap.Width,bitmap.Height));
            using var resized=new Bitmap(bitmap,new Size(Math.Max(1,(int)(bitmap.Width*ratio)),Math.Max(1,(int)(bitmap.Height*ratio))));
            using var stream=new MemoryStream();resized.Save(stream,ImageFormat.Png);
            if(stream.Length<=8*1024*1024)return stream.ToArray();
        }
        throw new InvalidOperationException("CaptureTooLarge");
    }
}
