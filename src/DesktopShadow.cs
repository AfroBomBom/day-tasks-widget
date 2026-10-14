using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

sealed class DesktopShadow : NativeWindow, IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend { public byte Operation,Flags,Alpha,Format; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref Point position,ref Size size,IntPtr src,ref Point source,uint key,ref Blend blend,uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    Bitmap bitmap;
    public bool Visible { get { return Handle!=IntPtr.Zero && IsWindowVisible(Handle); } }
    public bool AlignedWith(IntPtr widget) {
        Rect main,shade; GetWindowRect(widget,out main); GetWindowRect(Handle,out shade);
        return shade.Left==main.Left-26 && shade.Top==main.Top-26 && shade.Right==main.Right+26 && shade.Bottom==main.Bottom+26;
    }
    public void Update(IntPtr widget,IntPtr parent,int width,int height) {
        const int margin=26;
        if(Handle==IntPtr.Zero) CreateHandle(new CreateParams { Caption="Тень списка дел", Parent=parent,Style=0x40000000|0x10000000,ExStyle=0x00080000|0x08000000|0x00000020,Width=width+margin*2,Height=height+margin*2 });
        if(bitmap==null || bitmap.Width!=width+margin*2 || bitmap.Height!=height+margin*2) {
            if(bitmap!=null) bitmap.Dispose();
            bitmap=new Bitmap(width+margin*2,height+margin*2,PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(bitmap)) {
                g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                for(int spread=22;spread>=0;spread--) {
                    using(var path=RoundedPanel.Shape(width+spread*2,height+spread*2,22+spread)) {
                        using(var matrix=new Matrix()) { matrix.Translate(margin-spread,margin-spread+5); path.Transform(matrix); }
                        using(var brush=new SolidBrush(Color.FromArgb(spread>15 ? 2 : spread>7 ? 4 : 7,12,23,46))) g.FillPath(brush,path);
                    }
                }
            }
        }
        Rect rect; GetWindowRect(widget,out rect); Point position=new Point(rect.Left-margin,rect.Top-margin),source=Point.Empty;
        Size size=bitmap.Size; Blend blend=new Blend { Alpha=255,Format=1 };
        IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),image=bitmap.GetHbitmap(Color.FromArgb(0)),old=SelectObject(memory,image);
        try {
            if(!UpdateLayeredWindow(Handle,screen,ref position,ref size,memory,ref source,0,ref blend,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SetWindowPos(Handle,widget,0,0,0,0,0x0013|0x0040);
        } finally { SelectObject(memory,old); DeleteObject(image); DeleteDC(memory); ReleaseDC(IntPtr.Zero,screen); }
    }
    public void Dispose() { if(Handle!=IntPtr.Zero) DestroyHandle(); if(bitmap!=null) { bitmap.Dispose(); bitmap=null; } }
}
