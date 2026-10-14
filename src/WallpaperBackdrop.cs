using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

static class WallpaperBackdrop {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool SystemParametersInfo(uint action,uint size,StringBuilder text,uint flags);
    static Bitmap backdrop;
    static string cacheKey="";
    static Rectangle monitor;
    public static bool Loaded { get { return backdrop!=null; } }
    public static void Refresh(Control control) {
        Rectangle screen=Screen.FromPoint(control.PointToScreen(Point.Empty)).Bounds;
        var text=new StringBuilder(2048); SystemParametersInfo(0x0073,(uint)text.Capacity,text,0);
        string path=text.ToString();
        if(!File.Exists(path)) path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Windows","Themes","TranscodedWallpaper");
        string style="10",tile="0";
        try { using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop")) { style=Convert.ToString(key.GetValue("WallpaperStyle","10")); tile=Convert.ToString(key.GetValue("TileWallpaper","0")); } } catch { }
        string stamp=File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString() : "none";
        string id=path+stamp+screen+style+tile; if(id==cacheKey) return;
        if(backdrop!=null) { backdrop.Dispose(); backdrop=null; } monitor=screen; cacheKey=id;
        int w=Math.Max(1,screen.Width/4),h=Math.Max(1,screen.Height/4);
        using(var canvas=new Bitmap(w,h,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(canvas)) {
                g.Clear(SystemColors.Desktop); g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                try {
                    using(var source=Image.FromFile(path)) {
                        if(tile=="1") { using(var brush=new TextureBrush(source)) { brush.ScaleTransform(.25f,.25f); g.FillRectangle(brush,0,0,w,h); } }
                        else {
                            float scale=style=="6" ? Math.Min((float)w/source.Width,(float)h/source.Height) : Math.Max((float)w/source.Width,(float)h/source.Height);
                            if(style=="2") g.DrawImage(source,new Rectangle(0,0,w,h));
                            else if(style=="0") g.DrawImage(source,new RectangleF((w-source.Width/4f)/2,(h-source.Height/4f)/2,source.Width/4f,source.Height/4f));
                            else g.DrawImage(source,new RectangleF((w-source.Width*scale)/2,(h-source.Height*scale)/2,source.Width*scale,source.Height*scale));
                        }
                    }
                } catch {
                    using(var fill=new LinearGradientBrush(new Rectangle(0,0,w,h),Color.FromArgb(36,71,63),Color.FromArgb(61,83,105),45f)) g.FillRectangle(fill,0,0,w,h);
                }
            }
            backdrop=Blur(canvas,7);
        }
    }
    public static void Draw(Control control,Graphics g) {
        if(backdrop==null) Refresh(control);
        Point origin=control.PointToScreen(Point.Empty);
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
        g.DrawImage(backdrop,new Rectangle(monitor.Left-origin.X,monitor.Top-origin.Y,monitor.Width,monitor.Height));
    }
    public static Bitmap Blur(Bitmap source,int radius) {
        int w=source.Width,h=source.Height; var rect=new Rectangle(0,0,w,h);
        var bytes=new byte[w*h*4]; var read=source.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try { for(int y=0;y<h;y++) Marshal.Copy(IntPtr.Add(read.Scan0,y*read.Stride),bytes,y*w*4,w*4); } finally { source.UnlockBits(read); }
        for(int pass=0;pass<3;pass++) {
            var horizontal=new byte[bytes.Length]; var vertical=new byte[bytes.Length]; int count=radius*2+1;
            for(int y=0;y<h;y++) for(int c=0;c<3;c++) {
                int sum=0; for(int k=-radius;k<=radius;k++) sum+=bytes[(y*w+Math.Max(0,Math.Min(w-1,k)))*4+c];
                for(int x=0;x<w;x++) { horizontal[(y*w+x)*4+c]=(byte)(sum/count); sum-=bytes[(y*w+Math.Max(0,x-radius))*4+c]; sum+=bytes[(y*w+Math.Min(w-1,x+radius+1))*4+c]; }
            }
            for(int x=0;x<w;x++) for(int c=0;c<3;c++) {
                int sum=0; for(int k=-radius;k<=radius;k++) sum+=horizontal[(Math.Max(0,Math.Min(h-1,k))*w+x)*4+c];
                for(int y=0;y<h;y++) { vertical[(y*w+x)*4+c]=(byte)(sum/count); sum-=horizontal[(Math.Max(0,y-radius)*w+x)*4+c]; sum+=horizontal[(Math.Min(h-1,y+radius+1)*w+x)*4+c]; }
            }
            for(int i=3;i<vertical.Length;i+=4) vertical[i]=255; bytes=vertical;
        }
        var result=new Bitmap(w,h,PixelFormat.Format32bppArgb); var write=result.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try { for(int y=0;y<h;y++) Marshal.Copy(bytes,y*w*4,IntPtr.Add(write.Scan0,y*write.Stride),w*4); } finally { result.UnlockBits(write); }
        return result;
    }
}
