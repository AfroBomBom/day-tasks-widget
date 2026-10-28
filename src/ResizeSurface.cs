using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A complete, already painted frame is submitted atomically during resizing.
sealed class ResizeSurface : NativeWindow,IDisposable {
    [StructLayout(LayoutKind.Sequential,Pack=1)] struct Blend { public byte Operation,Flags,Alpha,Format; }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll",SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr dst,ref Point position,ref Size size,IntPtr src,ref Point source,uint key,ref Blend blend,uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    readonly IntPtr desktop;
    readonly Bitmap before;
    Bitmap after,frame;
    public Size FrameSize { get; private set; }
    public Point FrameLocation { get; private set; }
    public ResizeSurface(IntPtr parent,Bitmap initial) {
        desktop=parent; before=initial;
        CreateHandle(new CreateParams { Caption="Анимация списка дел",Parent=parent,Style=0x40000000|0x10000000,ExStyle=0x80000|0x08000000,Width=initial.Width,Height=initial.Height });
    }
    public void SetTarget(Bitmap target) { if(after!=null) after.Dispose(); after=target; }
    public Bitmap CaptureFrame() { return (Bitmap)frame.Clone(); }
    static void Draw(Graphics g,Bitmap image,float alpha) {
        if(alpha<=0) return;
        using(var attributes=new ImageAttributes()) {
            attributes.SetColorMatrix(new ColorMatrix { Matrix33=alpha });
            g.DrawImage(image,new Rectangle(0,0,image.Width,image.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel,attributes);
        }
    }
    public void Present(Size size,Point location,double progress,byte alpha,Color background) {
        if(frame!=null) frame.Dispose(); frame=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(frame)) {
            g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=RoundedPanel.Shape(size.Width,size.Height,22)) {
                using(var fill=new SolidBrush(background)) g.FillPath(fill,shape);
                g.SetClip(shape); Draw(g,before,1-(float)progress); Draw(g,after ?? before,(float)progress);
            }
        }
        Point position=location,source=Point.Empty; ClientToScreen(desktop,ref position);
        IntPtr screen=GetDC(IntPtr.Zero),memory=CreateCompatibleDC(screen),image=frame.GetHbitmap(Color.FromArgb(0)),old=SelectObject(memory,image);
        var blend=new Blend { Alpha=alpha,Format=1 };
        try {
            if(!UpdateLayeredWindow(Handle,screen,ref position,ref size,memory,ref source,0,ref blend,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0013|0x0040);
            FrameSize=size; FrameLocation=location;
        } finally { SelectObject(memory,old); DeleteObject(image); DeleteDC(memory); ReleaseDC(IntPtr.Zero,screen); }
    }
    public void Dispose() {
        if(Handle!=IntPtr.Zero) DestroyHandle();
        before.Dispose(); if(after!=null) after.Dispose(); if(frame!=null) frame.Dispose();
    }
}
