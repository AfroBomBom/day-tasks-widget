using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.Threading;
using System.Runtime.InteropServices;

[Serializable] public class TaskItem {
    public string Id = Guid.NewGuid().ToString();
    public string Text = "";
    public bool Done;
    public bool Important=true;
    public bool Urgent;
    public bool InMatrix;
    public DateTime CompletedAt;
}
[Serializable] public class TaskData { public List<TaskItem> Tasks = new List<TaskItem>(); }
public static class Storage {
    public static DateTime Boundary(DateTime now) { return now.Date.AddHours(3) > now ? now.Date.AddDays(-1).AddHours(3) : now.Date.AddHours(3); }
    public static int Clean(TaskData data, DateTime now) { DateTime b = Boundary(now); return data.Tasks.RemoveAll(delegate(TaskItem t) { return t.Done && t.CompletedAt < b; }); }
    public static TaskData Read(string path) {
        if (!File.Exists(path)) return new TaskData();
        using (var stream = File.OpenRead(path)) return (TaskData)new XmlSerializer(typeof(TaskData)).Deserialize(stream);
    }
    public static void Write(string path, TaskData data) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
            new XmlSerializer(typeof(TaskData)).Serialize(stream, data); stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
    }
}
public class DayTasks : Form {
    class Palette {
        public string Id, Name; public Color Bg,Card,Ink,Muted,Accent;
        public Palette(string id,string name,string bg,string card,string ink,string muted,string accent) {
            Id=id; Name=name; Bg=ColorTranslator.FromHtml(bg); Card=ColorTranslator.FromHtml(card); Ink=ColorTranslator.FromHtml(ink); Muted=ColorTranslator.FromHtml(muted); Accent=ColorTranslator.FromHtml(accent);
        }
    }
    static readonly Palette[] Palettes={
        new Palette("blue","Синий · ночь","#101E32","#1B304B","#ECF4FF","#95AEC9","#7BBFF8"),
        new Palette("glass","Жидкое стекло · прозрачное","#20302B","#2D403A","#FFFFFF","#D5E2DD","#D4F4E4"),
        new Palette("glass-blue","Стекло · ледяное","#142B42","#263E56","#FFFFFF","#D0E0F0","#A2D7FF"),
        new Palette("glass-warm","Стекло · дымчатое","#36272C","#49353C","#FFF8F1","#E0CDCE","#FFD4AB"),
        new Palette("glass-light","Стекло · светлое","#E5ECEF","#F1F5F6","#132838","#40596A","#295FAA"),
        new Palette("apple","Светлый голубой","#F2F2F7","#FFFFFF","#1C1C1E","#63636B","#007AFF"),
        new Palette("dark","Тёмный · мята","#161922","#202531","#EEF1F7","#8F9BB3","#90D0AD"),
        new Palette("lavender","Лавандовый","#F3EFF9","#FFFFFF","#342B48","#76688A","#8055BD")
    };
    Palette palette;
    public bool IsGlass { get { return palette.Id.StartsWith("glass"); } }
    public bool LightGlass { get { return palette.Id=="glass-light"; } }
    public Color GlassTint { get { return palette.Bg; } }
    Color Bg { get { return palette.Bg; } } Color Card { get { return palette.Card; } }
    Color Ink { get { return palette.Ink; } } Color Muted { get { return palette.Muted; } } Color Accent { get { return palette.Accent; } }
    readonly string themePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Данные","оформление.txt");
    readonly string dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Данные", "дела.xml");
    TaskData data;
    FlowLayoutPanel list;
    BackdropPanel matrix;
    FlowLayoutPanel[] quadrants=new FlowLayoutPanel[4];
    Button listTab,matrixTab;
    Label carryNote,cleanupNote;
    Label entryHint;
    bool matrixView;
    static readonly string[] QuadrantNames={"Важно · срочно","Важно · не срочно","Не важно · срочно","Не важно · не срочно"};
    static int Quadrant(TaskItem task) { return !task.InMatrix ? -1 : task.Important ? (task.Urgent ? 0 : 1) : (task.Urgent ? 2 : 3); }
    static void SetQuadrant(TaskItem task,int target) { task.InMatrix=target>=0; if(target>=0) { task.Important=target<2; task.Urgent=target==0 || target==2; } }
    TextBox input;
    Label count, date;
    System.Windows.Forms.Timer timer;
    System.Windows.Forms.Timer resizeTimer;
    readonly System.Diagnostics.Stopwatch resizeClock=new System.Diagnostics.Stopwatch();
    Size resizeFrom,resizeTo;
    Point resizeOrigin,resizeDestination;
    bool resizeFrame;
    ResizeSurface resizeSurface;
    [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr hwnd,IntPtr rect,IntPtr region,uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();
    bool readFailed;
    Func<bool> saveOverride;
    IntPtr desktop;
    DesktopShadow shadow;
    byte glassAlpha=220;
    Point dragStart, dragOrigin;
    readonly string positionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Данные","позиция.txt");
    delegate bool EnumWindowProc(IntPtr hwnd,IntPtr param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string className,string title);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr param);
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint key,byte alpha,uint flags);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr hwnd,out uint key,out byte alpha,out uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd,ref Point point);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int size);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    public DayTasks(bool preview,string previewTheme=null) {
        string theme="blue";
        if(preview) { if(previewTheme!=null) theme=previewTheme; }
        else { try { theme=File.ReadAllText(themePath).Trim(); } catch { } }
        palette=FindPalette(theme);
        Font = new Font("Segoe UI", 10); BackColor = Bg; ForeColor = Ink;
        SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);
        Text = "Дела на день"; FormBorderStyle = FormBorderStyle.None; ClientSize = new Size(410, 606);
        StartPosition = FormStartPosition.Manual;
        Rectangle area=Screen.PrimaryScreen.WorkingArea;
        Location=new Point(area.Right-Width-24,area.Top+30);
        ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var header = new BackdropPanel { Location = new Point(2,2), Size = new Size(406,104), BackColor = Bg,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
        Controls.Add(header);
        var title = LabelAt("Дела на день", 22, 24, 310, 35, new Font("Segoe UI", 19, FontStyle.Bold), Ink);
        header.Controls.Add(title);
        date = LabelAt("", 24, 68, 220, 26, new Font("Segoe UI",9), Muted); header.Controls.Add(date);
        var themes=ButtonAt("Оформление ▾",252,64,136,30,Card,Ink); themes.Font=new Font("Segoe UI",9); themes.Anchor=AnchorStyles.Top|AnchorStyles.Right; header.Controls.Add(themes);
        themes.Click+=delegate {
            var menu=new ContextMenuStrip { BackColor=Card,ForeColor=Ink,Font=new Font("Segoe UI",10),ShowImageMargin=false,ShowCheckMargin=true };
            foreach(Palette choice in Palettes) {
                var item=new ToolStripMenuItem(choice.Name) { Checked=choice.Id==palette.Id };
                item.Click+=delegate { ChooseTheme(choice); }; menu.Items.Add(item);
            }
            menu.Closed+=delegate { BeginInvoke(new MethodInvoker(delegate { menu.Dispose(); })); };
            menu.Show(themes,new Point(0,themes.Height));
        };
        MouseEventHandler startDrag=delegate(object sender,MouseEventArgs e) {
            if(e.Button!=MouseButtons.Left) return;
            dragStart=Cursor.Position; dragOrigin=Location; ((Control)sender).Capture=true;
        };
        MouseEventHandler moveDrag=delegate(object sender,MouseEventArgs e) {
            if(!((Control)sender).Capture || e.Button!=MouseButtons.Left) return;
            Point now=Cursor.Position; Location=new Point(dragOrigin.X+now.X-dragStart.X,dragOrigin.Y+now.Y-dragStart.Y);
        };
        MouseEventHandler endDrag=delegate(object sender,MouseEventArgs e) {
            ((Control)sender).Capture=false; if(desktop!=IntPtr.Zero) SavePosition();
        };
        foreach(Control control in new Control[] { header,title,date }) {
            control.MouseDown+=startDrag; control.MouseMove+=moveDrag; control.MouseUp+=endDrag;
        }
        var close=ButtonAt("×",365,22,26,28,Card,Muted); close.Anchor=AnchorStyles.Top|AnchorStyles.Right; header.Controls.Add(close); close.Click+=delegate { Close(); };
        var entry = new RoundedPanel { Location = new Point(22,115), Size = new Size(366,51), BackColor = Card,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
        Controls.Add(entry);
        input = new TextBox { BorderStyle = BorderStyle.None, BackColor = Card, ForeColor = Ink, Location = new Point(13,15), Size = new Size(290,27), Font = new Font("Segoe UI",11), MaxLength = 500,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
        entry.Controls.Add(input);
        var add = ButtonAt("+",315,7,42,37,Accent,IsGlass ? Ink : Bg); add.Font = new Font("Segoe UI",20); add.Anchor=AnchorStyles.Top|AnchorStyles.Right; entry.Controls.Add(add);
        add.Click += delegate { AddTask(); };
        input.KeyDown += delegate(object sender, KeyEventArgs e) { if(e.KeyCode == Keys.Enter) { e.SuppressKeyPress=true; AddTask(); } };
        entryHint=LabelAt("Напишите дело и нажмите Enter",24,172,365,22,new Font("Segoe UI",9),Muted); Controls.Add(entryHint);
        count = LabelAt("",24,209,360,23,new Font("Segoe UI",10,FontStyle.Bold),Accent); Controls.Add(count);
        list = new BackdropFlow { Location=new Point(22,244),Size=new Size(376,281), FlowDirection=FlowDirection.TopDown,WrapContents=false, AutoScroll=true, BackColor=Bg,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right };
        Controls.Add(list);
        carryNote=LabelAt("Незавершённые дела остаются на завтра.",24,550,366,20,new Font("Segoe UI",9),Muted); Controls.Add(carryNote);
        cleanupNote=LabelAt("Выполненные удаляются в 03:00 утра.",24,573,366,20,new Font("Segoe UI",9),Muted); Controls.Add(cleanupNote);
        foreach(Control control in Controls) if(control!=header) control.Top+=42;
        ClientSize=new Size(410,648);
        listTab=ButtonAt("Список дел",22,108,112,32,Accent,Bg); Controls.Add(listTab);
        matrixTab=ButtonAt("Матрица Эйзенхауэра",142,108,246,32,Card,Ink); Controls.Add(matrixTab);
        listTab.Click+=delegate { SwitchView(false,true); }; matrixTab.Click+=delegate { SwitchView(true,true); };
        matrix=new BackdropPanel { Location=new Point(22,154),Size=new Size(556,490),BackColor=Bg,Visible=false }; Controls.Add(matrix);
        for(int i=0;i<4;i++) {
            int target=i;
            var cell=new RoundedPanel { Location=new Point((i%2)*283,(i/2)*250),Size=new Size(273,240),BackColor=Card };
            cell.Controls.Add(LabelAt(QuadrantNames[i],10,12,253,22,new Font("Segoe UI",10,FontStyle.Bold),Accent));
            quadrants[i]=new BackdropFlow { Location=new Point(7,48),Size=new Size(259,129),FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=Card };
            cell.Controls.Add(quadrants[i]); matrix.Controls.Add(cell);
            var draftHint=LabelAt("Новое задание · Enter",12,179,249,22,new Font("Segoe UI",8),Muted);
            draftHint.TextAlign=ContentAlignment.MiddleCenter; cell.Controls.Add(draftHint);
            var draftBox=new RoundedPanel { Location=new Point(12,203),Size=new Size(249,30),BackColor=Bg }; cell.Controls.Add(draftBox);
            var draft=new TextBox { Location=new Point(12,0),Size=new Size(225,22),BackColor=Bg,ForeColor=Ink,BorderStyle=BorderStyle.None,MaxLength=500,Font=new Font("Segoe UI",9),TextAlign=HorizontalAlignment.Center }; draftBox.Controls.Add(draft);
            draft.Top=(draftBox.ClientSize.Height-draft.Height)/2;
            draft.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { e.SuppressKeyPress=true; AddMatrixTask(target,draft); } };
        }
        if(preview) {
            data=new TaskData(); data.Tasks.Add(new TaskItem { Text="Записать планы на день",Urgent=true,InMatrix=true }); data.Tasks.Add(new TaskItem { Text="Прогуляться на свежем воздухе",InMatrix=true }); data.Tasks.Add(new TaskItem { Text="Выпить стакан воды",Important=false,InMatrix=true,Done=true,CompletedAt=DateTime.Now });
        } else {
            try { data=Storage.Read(dataPath); }
            catch(Exception ex) {
                try { data=Storage.Read(dataPath+".bak"); MessageBox.Show("Восстановлен резервный список дел.\n"+ex.Message,Text); }
                catch { data=new TaskData(); readFailed=true; MessageBox.Show("Не удалось прочитать сохранённые дела. Файл сохранён без изменений.\n"+ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error); }
            }
            if(!readFailed && Storage.Clean(data,DateTime.Now)>0) Save();
        }
        input.Enabled = !readFailed; add.Enabled = !readFailed;
        RefreshTasks();
        if(!preview) {
            timer=new System.Windows.Forms.Timer { Interval=15000 };
            timer.Tick+=delegate { if(!readFailed && Storage.Clean(data,DateTime.Now)>0) { Save(); RefreshTasks(); } UpdateDate(); if(IsGlass) { WallpaperBackdrop.Refresh(this); RefreshGlassInput(); Invalidate(true); } }; timer.Start();
        }
        FormClosed+=delegate { if(timer!=null) timer.Dispose(); if(resizeTimer!=null) resizeTimer.Dispose(); if(resizeSurface!=null) resizeSurface.Dispose(); if(shadow!=null) shadow.Dispose(); };
        Round(this,22);
        if(IsGlass) { WallpaperBackdrop.Refresh(this); RefreshGlassInput(); }
    }
    static double ResizeEase(double progress) { double t=Math.Max(0,Math.Min(1,progress)); return t*t*t*(t*(t*6-15)+10); }
    void SwitchView(bool showMatrix,bool animate=false) {
        if(resizeTimer!=null) resizeTimer.Stop();
        ResizeSurface previousSurface=resizeSurface;
        Size visualSize=previousSurface==null ? ClientSize : previousSurface.FrameSize;
        Point visualLocation=previousSurface==null ? Location : previousSurface.FrameLocation;
        bool useSurface=animate && Visible && desktop!=IntPtr.Zero;
        Bitmap initial=useSurface ? (previousSurface==null ? CaptureView() : previousSurface.CaptureFrame()) : null;
        resizeSurface=null;
        matrixView=showMatrix; list.Visible=!showMatrix; matrix.Visible=showMatrix;
        input.Parent.Visible=!showMatrix; entryHint.Visible=!showMatrix; count.Visible=!showMatrix;
        listTab.BackColor=showMatrix ? Card : Accent; listTab.ForeColor=showMatrix || IsGlass ? Ink : Bg;
        matrixTab.BackColor=showMatrix ? Accent : Card; matrixTab.ForeColor=!showMatrix || IsGlass ? Ink : Bg;
        ((SoftButton)listTab).Emphasized=!showMatrix; ((SoftButton)matrixTab).Emphasized=showMatrix;
        resizeFrom=visualSize; resizeTo=new Size(showMatrix ? 600 : 410,showMatrix ? 724 : 648);
        resizeOrigin=visualLocation; resizeDestination=visualLocation;
        if(desktop!=IntPtr.Zero) {
            Rectangle screen=Screen.FromHandle(Handle).WorkingArea; Point origin=PointToScreen(Point.Empty);
            resizeDestination.Offset(-Math.Max(0,origin.X+resizeTo.Width-screen.Right),-Math.Max(0,origin.Y+resizeTo.Height-screen.Bottom));
        }
        if(!animate || !Visible || resizeFrom==resizeTo) {
            if(initial!=null) initial.Dispose(); ApplyResizeFrame(1); FinishResize(); if(previousSurface!=null) previousSurface.Dispose(); return;
        }
        if(useSurface) {
            try {
                resizeSurface=new ResizeSurface(desktop,initial); initial=null;
                resizeSurface.Present(resizeFrom,resizeOrigin,0,IsGlass ? glassAlpha : (byte)255,Bg);
                SetWindowLong(Handle,-20,GetWindowLong(Handle,-20)|0x80000);
                if(!SetLayeredWindowAttributes(Handle,0,0,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                ApplyLiveResizeFrame(1,false);
                resizeSurface.SetTarget(CaptureView());
                if(shadow!=null) shadow.Update(resizeSurface.Handle,desktop,resizeFrom.Width,resizeFrom.Height);
            } catch {
                if(initial!=null) initial.Dispose(); ApplyLiveResizeFrame(1,false); FinishResize();
                if(previousSurface!=null) previousSurface.Dispose(); return;
            }
        }
        if(previousSurface!=null) previousSurface.Dispose();
        if(resizeTimer==null) {
            resizeTimer=new System.Windows.Forms.Timer { Interval=15 };
            resizeTimer.Tick+=delegate {
                double progress=Math.Min(1,resizeClock.Elapsed.TotalMilliseconds/340.0);
                ApplyResizeFrame(ResizeEase(progress));
                if(progress>=1) { resizeTimer.Stop(); FinishResize(); }
            };
        }
        resizeClock.Restart(); resizeTimer.Start(); Invalidate(true);
    }
    Bitmap CaptureView() {
        PerformLayout();
        var image=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try { DrawToBitmap(image,ClientRectangle); return image; } catch { image.Dispose(); throw; }
    }
    void ApplyResizeFrame(double progress) {
        if(resizeSurface!=null) {
            Size size=new Size((int)Math.Round(resizeFrom.Width+(resizeTo.Width-resizeFrom.Width)*progress),(int)Math.Round(resizeFrom.Height+(resizeTo.Height-resizeFrom.Height)*progress));
            Point position=new Point((int)Math.Round(resizeOrigin.X+(resizeDestination.X-resizeOrigin.X)*progress),(int)Math.Round(resizeOrigin.Y+(resizeDestination.Y-resizeOrigin.Y)*progress));
            resizeSurface.Present(size,position,progress,IsGlass ? glassAlpha : (byte)255,Bg);
            if(shadow!=null) shadow.Update(resizeSurface.Handle,desktop,size.Width,size.Height);
            return;
        }
        ApplyLiveResizeFrame(progress,true);
    }
    void ApplyLiveResizeFrame(double progress,bool updateShadow) {
        resizeFrame=true; SuspendLayout();
        try {
            ClientSize=new Size((int)Math.Round(resizeFrom.Width+(resizeTo.Width-resizeFrom.Width)*progress),(int)Math.Round(resizeFrom.Height+(resizeTo.Height-resizeFrom.Height)*progress));
            Location=new Point((int)Math.Round(resizeOrigin.X+(resizeDestination.X-resizeOrigin.X)*progress),(int)Math.Round(resizeOrigin.Y+(resizeDestination.Y-resizeOrigin.Y)*progress));
            carryNote.Top=ClientSize.Height-(matrixView ? 58 : 56); cleanupNote.Top=ClientSize.Height-33;
            Round(this,22);
        } finally { ResumeLayout(true); resizeFrame=false; }
        if(updateShadow && desktop!=IntPtr.Zero && shadow!=null) shadow.Update(Handle,desktop,Width,Height);
        if(IsGlass) RefreshGlassInput(); Invalidate(true);
    }
    void FinishResize() {
        resizeClock.Stop();
        if(desktop!=IntPtr.Zero) {
            UpdateGlassEffects(); RedrawWindow(Handle,IntPtr.Zero,IntPtr.Zero,0x0185);
            DwmFlush(); SavePosition();
        }
        if(resizeSurface!=null) { resizeSurface.Dispose(); resizeSurface=null; }
    }
    void AddMatrixTask(int target,TextBox draft) {
        string text=draft.Text.Trim(); if(readFailed || text.Length==0) return;
        var task=new TaskItem { Text=text }; SetQuadrant(task,target); data.Tasks.Add(task);
        if(!Save()) { data.Tasks.Remove(task); return; }
        draft.Clear(); RefreshTasks(); draft.Focus();
    }
    void AssignMatrix(TaskItem task,int target) {
        if(readFailed) return;
        bool assigned=task.InMatrix,important=task.Important,urgent=task.Urgent;
        SetQuadrant(task,target);
        if(!Save()) { task.InMatrix=assigned; task.Important=important; task.Urgent=urgent; } RefreshTasks();
    }
    void ShowTaskMenu(TaskItem task,Control anchor) {
        if(readFailed) return;
        var menu=new ContextMenuStrip { BackColor=Card,ForeColor=Ink,ShowCheckMargin=true,ShowImageMargin=false };
        for(int i=0;i<4;i++) {
            int target=i;
            var item=new ToolStripMenuItem(QuadrantNames[i]) { Checked=Quadrant(task)==i };
            item.Click+=delegate {
                AssignMatrix(task,target);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add("Убрать из матрицы",null,delegate { AssignMatrix(task,-1); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Удалить дело",null,delegate { int index=data.Tasks.IndexOf(task); data.Tasks.Remove(task); if(!Save()) data.Tasks.Insert(index,task); RefreshTasks(); });
        menu.Closed+=delegate { BeginInvoke(new MethodInvoker(delegate { menu.Dispose(); })); }; menu.Show(anchor,new Point(0,anchor.Height));
    }
    void RefreshGlassInput() {
        if(input==null || !IsGlass) return;
        using(var sample=new Bitmap(input.Width,input.Height)) using(var g=Graphics.FromImage(sample)) { GlassPaint.Background(input,g); input.BackColor=sample.GetPixel(input.Width/2,input.Height/2); }
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        if(IsGlass) GlassPaint.Background(this,e.Graphics); else base.OnPaintBackground(e);
    }
    protected override void OnLocationChanged(EventArgs e) {
        base.OnLocationChanged(e); if(!resizeFrame && desktop!=IntPtr.Zero && shadow!=null) { shadow.Update(Handle,desktop,Width,Height); WallpaperBackdrop.Refresh(this); RefreshGlassInput(); Invalidate(true); }
    }
    void UpdateGlassEffects() {
        if(desktop==IntPtr.Zero) return;
        int style=GetWindowLong(Handle,-20);
        if(IsGlass) {
            SetWindowLong(Handle,-20,style|0x00080000);
            if(!SetLayeredWindowAttributes(Handle,0,glassAlpha,2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if(shadow==null) shadow=new DesktopShadow(); shadow.Update(Handle,desktop,Width,Height);
        } else {
            if(shadow!=null) { shadow.Dispose(); shadow=null; }
            if((style&0x00080000)!=0) { SetWindowLong(Handle,-20,style&~0x00080000); Invalidate(true); }
        }
    }
    static Palette FindPalette(string id) { foreach(Palette p in Palettes) if(p.Id==id) return p; return Palettes[0]; }
    static void Round(Control control,int radius) {
        int d=radius*2; using(var path=new GraphicsPath()) {
            path.AddArc(0,0,d,d,180,90); path.AddArc(control.Width-d,0,d,d,270,90);
            path.AddArc(control.Width-d,control.Height-d,d,d,0,90); path.AddArc(0,control.Height-d,d,d,90,90); path.CloseFigure();
            Region previous=control.Region; control.Region=new Region(path); if(previous!=null) previous.Dispose();
        }
    }
    void ChooseTheme(Palette next) {
        if(next.Id==palette.Id) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(themePath)); File.WriteAllText(themePath,next.Id); }
        catch(Exception ex) { MessageBox.Show("Не удалось сохранить оформление.\n"+ex.Message,Text); return; }
        ApplyTheme(next);
    }
    static Color Recolor(Color color,Palette old,Palette next) {
        if(color.ToArgb()==old.Bg.ToArgb()) return next.Bg;
        if(color.ToArgb()==old.Card.ToArgb()) return next.Card;
        if(color.ToArgb()==old.Ink.ToArgb()) return next.Ink;
        if(color.ToArgb()==old.Muted.ToArgb()) return next.Muted;
        if(color.ToArgb()==old.Accent.ToArgb()) return next.Accent;
        return color;
    }
    void RecolorControls(Control control,Palette old,Palette next) {
        control.BackColor=Recolor(control.BackColor,old,next); control.ForeColor=Recolor(control.ForeColor,old,next);
        foreach(Control child in control.Controls) RecolorControls(child,old,next);
    }
    void ApplyTheme(Palette next) {
        Palette old=palette; SuspendLayout(); RecolorControls(this,old,next); palette=next;
        SetSymbols(this);
        input.BackColor=Card; RefreshTasks(); ResumeLayout(); if(IsGlass) { WallpaperBackdrop.Refresh(this); RefreshGlassInput(); } UpdateGlassEffects(); Invalidate(true);
        SwitchView(matrixView);
        if(desktop!=IntPtr.Zero) { try { WriteDiagnostics(Path.Combine(Path.GetDirectoryName(dataPath),"состояние.txt")); } catch { } }
    }
    void SetSymbols(Control control) { foreach(Control child in control.Controls) { if(child is SoftButton && child.Text=="+") child.ForeColor=IsGlass ? Ink : Bg; SetSymbols(child); } }
    void RestoreWindow() {
        if(desktop==IntPtr.Zero) AttachToDesktop();
        Show(); ShowWindow(Handle,5); BringToFront(); input.Focus();
    }
    static IntPtr DesktopHost() {
        IntPtr host=FindWindowEx(FindWindow("Progman",null),IntPtr.Zero,"SHELLDLL_DefView",null);
        if(host==IntPtr.Zero) EnumWindows(delegate(IntPtr window,IntPtr param) {
            host=FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null); return host==IntPtr.Zero;
        },IntPtr.Zero);
        return host;
    }
    void AttachToDesktop() {
        desktop=DesktopHost();
        if(desktop==IntPtr.Zero) throw new InvalidOperationException("Не найден рабочий стол Windows. Запустите виджет, когда появится рабочий стол.");
        Point screen=Location;
        try { string[] xy=File.ReadAllText(positionPath).Split(','); screen=new Point(int.Parse(xy[0]),int.Parse(xy[1])); } catch { }
        bool onScreen=false;
        foreach(Screen monitor in Screen.AllScreens) if(monitor.WorkingArea.Contains(new Rectangle(screen,Size))) onScreen=true;
        if(!onScreen) { Rectangle area=Screen.PrimaryScreen.WorkingArea; screen=new Point(area.Right-Width-24,area.Top+30); }
        SetWindowLong(Handle,-16,(GetWindowLong(Handle,-16)&unchecked((int)~0x80000000))|0x40000000);
        SetParent(Handle,desktop);
        if(GetParent(Handle)!=desktop) throw new InvalidOperationException("Не удалось прикрепить виджет к рабочему столу.");
        ScreenToClient(desktop,ref screen);
        SetWindowPos(Handle,IntPtr.Zero,screen.X,screen.Y,Width,Height,0x0020|0x0040);
        UpdateGlassEffects();
        if(IsGlass) { WallpaperBackdrop.Refresh(this); RefreshGlassInput(); Invalidate(true); }
        try { Directory.CreateDirectory(Path.GetDirectoryName(dataPath)); WriteDiagnostics(Path.Combine(Path.GetDirectoryName(dataPath),"состояние.txt")); } catch { }
    }
    void SavePosition() {
        try {
            Point p=PointToScreen(Point.Empty); Directory.CreateDirectory(Path.GetDirectoryName(positionPath));
            File.WriteAllText(positionPath,p.X+","+p.Y);
        } catch { }
    }
    void WriteDiagnostics(string path) {
        var name=new System.Text.StringBuilder(128); GetClassName(GetParent(Handle),name,name.Capacity);
        File.WriteAllText(path,"desktop="+name+"\nattached="+(GetParent(Handle)==desktop)+"\nvisible="+IsWindowVisible(Handle)+"\nchild="+((GetWindowLong(Handle,-16)&0x40000000)!=0)+"\ntaskbar="+ShowInTaskbar+"\ntheme="+palette.Id+"\nlocation="+PointToScreen(Point.Empty));
        uint key,flags; byte alpha;
        bool layered=GetLayeredWindowAttributes(Handle,out key,out alpha,out flags);
        File.AppendAllText(path,"\ntransparent="+(layered&&alpha<255)+"\nopacity="+(layered ? alpha.ToString() : "255")+"\nshadow="+(shadow!=null&&shadow.Visible));
    }
    Label LabelAt(string text,int x,int y,int w,int h,Font font,Color color) { return new Label { Text=text,Location=new Point(x,y),Size=new Size(w,h),Font=font,ForeColor=color,BackColor=Color.Transparent }; }
    Button ButtonAt(string text,int x,int y,int w,int h,Color back,Color fore) {
        var b=new SoftButton { Text=text,Location=new Point(x,y),Size=new Size(w,h),FlatStyle=FlatStyle.Flat,BackColor=back,ForeColor=fore,Cursor=Cursors.Hand,TabStop=false }; b.FlatAppearance.BorderSize=0; return b;
    }
    void UpdateDate() { date.Text=DateTime.Now.ToString("dddd, d MMMM",System.Globalization.CultureInfo.GetCultureInfo("ru-RU")); }
    bool Save() { if(saveOverride!=null) return saveOverride(); try { Storage.Write(dataPath,data); return true; } catch(Exception ex) { MessageBox.Show("Не удалось сохранить дела.\n"+ex.Message,Text,MessageBoxButtons.OK,MessageBoxIcon.Error); return false; } }
    void AddTask() {
        string text=input.Text.Trim(); if(text.Length==0 || readFailed) return;
        var t=new TaskItem { Text=text }; data.Tasks.Add(t);
        if(!Save()) { data.Tasks.Remove(t); return; }
        input.Clear(); RefreshTasks(); input.Focus();
    }
    void RefreshTasks() {
        UpdateDate(); int done=data.Tasks.FindAll(delegate(TaskItem t) { return t.Done; }).Count;
        count.Text=data.Tasks.Count==0 ? "Ваш день начинается здесь" : "Готово "+done+" из "+data.Tasks.Count;
        list.SuspendLayout(); while(list.Controls.Count>0) { var c=list.Controls[0]; list.Controls.Remove(c); c.Dispose(); }
        if(data.Tasks.Count==0) list.Controls.Add(LabelAt("Добавьте первое дело.\nДаже маленький шаг — это прогресс.",0,0,360,80,new Font("Segoe UI",11),Muted));
        foreach(TaskItem task in OrderedTasks(data)) list.Controls.Add(CreateTaskRow(task,false));
        list.ResumeLayout();
        for(int q=0;q<4;q++) {
            var group=quadrants[q]; group.SuspendLayout();
            while(group.Controls.Count>0) { var child=group.Controls[0]; group.Controls.Remove(child); child.Dispose(); }
            foreach(TaskItem task in OrderedTasks(data)) if(Quadrant(task)==q) group.Controls.Add(CreateTaskRow(task,true));
            if(group.Controls.Count==0) group.Controls.Add(LabelAt("Введите задание ниже",4,4,240,44,new Font("Segoe UI",9),Muted));
            group.ResumeLayout();
        }
    }
    Control CreateTaskRow(TaskItem task,bool compact) {
            var row=new RoundedPanel { Size=new Size(compact ? 239 : 352,compact ? 50 : 68),BackColor=Card,Margin=new Padding(0,0,0,9) };
            var check=new TaskCheck(task.Done,Card,Muted,Accent,Bg) { Location=new Point(compact ? 0 : 12,compact ? 11 : 20),Enabled=!readFailed };
            row.Controls.Add(check);
            var label=LabelAt(task.Text,compact ? 31 : 51,compact ? 8 : 12,compact ? 183 : 230,compact ? 35 : 45,new Font("Segoe UI",9,task.Done ? FontStyle.Strikeout : FontStyle.Regular),task.Done ? Muted : Ink); row.Controls.Add(label);
            var tooltip=new ToolTip(); tooltip.SetToolTip(label,task.Text); row.Disposed+=delegate { tooltip.Dispose(); };
            var remove=ButtonAt("×",319,20,24,28,Card,Muted); remove.Enabled=!readFailed; row.Controls.Add(remove);
            remove.Visible=!compact;
            var priority=ButtonAt("⋯",compact ? 215 : 287,compact ? 11 : 20,24,28,Card,Muted); priority.Enabled=!readFailed; row.Controls.Add(priority);
            priority.Click+=delegate { ShowTaskMenu(task,priority); }; tooltip.SetToolTip(priority,"Раздел матрицы и действия");
            check.Click+=delegate {
                bool old=task.Done; DateTime oldTime=task.CompletedAt; task.Done=!task.Done; task.CompletedAt=task.Done ? DateTime.Now : DateTime.MinValue;
                if(!Save()) { task.Done=old; task.CompletedAt=oldTime; } RefreshTasks();
            };
            remove.Click+=delegate { int index=data.Tasks.IndexOf(task); data.Tasks.Remove(task); if(!Save()) data.Tasks.Insert(index,task); RefreshTasks(); };
            row.Tag=task.Id; return row;
    }
    static List<TaskItem> OrderedTasks(TaskData source) {
        var ordered=source.Tasks.FindAll(delegate(TaskItem item) { return !item.Done; });
        ordered.AddRange(source.Tasks.FindAll(delegate(TaskItem item) { return item.Done; }));
        return ordered;
    }
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>0 && args[0]=="--self-test") return SelfTest(args[1]);
        if(args.Length>0 && args[0]=="--effects-test") return EffectsTest(args[1]);
        if(args.Length>0 && args[0]=="--preview") { using(var f=new DayTasks(true,args.Length>2 ? args[2] : null)) { if(args.Length>3 && args[3]=="matrix") { f.data.Tasks.Add(new TaskItem { Text="Ответить на письмо",Important=false,Urgent=true,InMatrix=true }); f.RefreshTasks(); f.SwitchView(true); } f.Show(); Application.DoEvents(); using(var bmp=new Bitmap(f.Width,f.Height)) { f.DrawToBitmap(bmp,new Rectangle(0,0,f.Width,f.Height)); bmp.Save(args[1]); } } return 0; }
        using(var wake=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexDayTasksWidgetWake")) {
            bool created;
            using(var mutex=new Mutex(true,"Local\\CodexDayTasksWidget",out created)) {
                if(!created) {
                    using(var current=System.Diagnostics.Process.GetCurrentProcess()) {
                        foreach(var other in System.Diagnostics.Process.GetProcessesByName(current.ProcessName)) {
                            using(other) { if(other.Id!=current.Id) AllowSetForegroundWindow(other.Id); }
                        }
                    }
                    wake.Set(); return 0;
                }
                using(var form=new DayTasks(false)) {
                    IntPtr ready=form.Handle;
                    var wait=ThreadPool.RegisterWaitForSingleObject(wake,delegate(object state,bool timedOut) {
                        try { if(!form.IsDisposed) form.BeginInvoke(new MethodInvoker(form.RestoreWindow)); }
                        catch(InvalidOperationException) { }
                    },null,Timeout.Infinite,false);
                    form.Shown+=delegate {
                        try {
                            form.RestoreWindow();
                            if(args.Length>1 && args[0]=="--diagnostics") form.WriteDiagnostics(args[1]);
                        } catch(Exception ex) { MessageBox.Show(ex.Message,"Дела на день",MessageBoxButtons.OK,MessageBoxIcon.Error); form.Close(); }
                    };
                    try { Application.Run(form); } finally { wait.Unregister(null); }
                }
            }
        }
        return 0;
    }
    static int EffectsTest(string folder) {
        Directory.CreateDirectory(folder);
        try {
            using(var window=new DayTasks(true,"glass")) {
                window.Show(); window.AttachToDesktop(); Application.DoEvents();
                uint key,flags; byte alpha;
                if(!GetLayeredWindowAttributes(window.Handle,out key,out alpha,out flags) || alpha!=220) throw new Exception("Glass opacity was not applied");
                if(window.shadow==null || !window.shadow.Visible || !window.shadow.AlignedWith(window.Handle)) throw new Exception("Shadow visibility or position");
                window.SwitchView(true,true);
                if(window.resizeSurface==null) throw new Exception("Resize must use an atomic frame surface");
                using(var painted=window.CaptureView()) {
                    int bright=0;
                    for(int y=22;y<58;y++) for(int x=22;x<240;x++) { Color pixel=painted.GetPixel(x,y); if(pixel.R>210 && pixel.G>210 && pixel.B>210) bright++; }
                    if(bright<30) throw new Exception("Prepared resize frame must contain fully painted text");
                }
                WaitForResize(window);
                if(window.Width!=600 || window.resizeSurface!=null || !window.shadow.AlignedWith(window.Handle)) throw new Exception("Expanded matrix shadow layout");
                window.SwitchView(false,true); WaitForResize(window);
                if(window.Width!=410 || !window.shadow.AlignedWith(window.Handle)) throw new Exception("Compact list shadow layout");
                window.SwitchView(true,true); Application.DoEvents(); window.SwitchView(false,true); WaitForResize(window);
                if(window.Width!=410 || window.matrixView || !window.shadow.AlignedWith(window.Handle)) throw new Exception("Interrupted animation must settle on latest tab");
                window.ApplyTheme(FindPalette("blue")); Application.DoEvents();
                if(window.shadow!=null || (GetWindowLong(window.Handle,-20)&0x80000)!=0) throw new Exception("Opaque theme did not reset transparency");
                window.ApplyTheme(FindPalette("glass")); Application.DoEvents();
                if(window.shadow==null || !window.shadow.Visible) throw new Exception("Shadow did not return");
                window.Close();
            }
            File.WriteAllText(Path.Combine(folder,"effects-result.txt"),"PASS: frosted wallpaper, native opacity 220/255, visible aligned shadow, theme reset, shadow restoration"); return 0;
        } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"effects-result.txt"),ex.ToString()); return 1; }
    }
    static void WaitForResize(DayTasks window) {
        var timeout=System.Diagnostics.Stopwatch.StartNew();
        while(window.resizeTimer!=null && window.resizeTimer.Enabled && timeout.ElapsedMilliseconds<2500) { Application.DoEvents(); Thread.Sleep(2); }
        if(window.resizeTimer!=null && window.resizeTimer.Enabled) throw new Exception("Resize animation timed out");
    }
    static int SelfTest(string folder) {
        try {
            if(ResizeEase(0)!=0 || ResizeEase(1)!=1) throw new Exception("Animation endpoints");
            double previous=0;
            for(int i=0;i<=100;i++) { double value=ResizeEase(i/100.0); if(value<previous || value>1) throw new Exception("Animation must not overshoot"); previous=value; }
            using(var source=new Bitmap(16,16)) {
                for(int y=0;y<16;y++) for(int x=0;x<16;x++) source.SetPixel(x,y,((x+y)%2==0) ? Color.Black : Color.White);
                using(var blurred=WallpaperBackdrop.Blur(source,2)) { Color center=blurred.GetPixel(8,8); if(center.R<70 || center.R>185 || center.A!=255) throw new Exception("Wallpaper blur did not soften texture"); }
            }
            using(var window=new DayTasks(true)) {
                if(window.palette.Id!="blue" || Palettes[0].Id!="blue") throw new Exception("Default theme must be blue");
                if(window.FormBorderStyle!=FormBorderStyle.None || window.ShowInTaskbar || window.MinimizeBox) throw new Exception("Desktop widget configuration");
                window.input.Text="Не потерять черновик";
                int taskCount=window.data.Tasks.Count;
                foreach(Palette theme in Palettes) {
                    window.ApplyTheme(theme);
                    window.SwitchView(true);
                    if(window.matrixTab.Width!=246 || window.matrixTab.ForeColor!=(window.IsGlass ? theme.Ink : theme.Bg)) throw new Exception("Matrix tab readability");
                    window.SwitchView(false);
                    if(window.BackColor!=theme.Bg || (!window.IsGlass && window.input.BackColor!=theme.Card) || window.input.ForeColor!=theme.Ink) throw new Exception("Theme colors");
                    if(window.input.Text!="Не потерять черновик" || window.data.Tasks.Count!=taskCount) throw new Exception("Theme changed tasks or draft");
                }
                window.data.Tasks[0].Done=true; window.RefreshTasks();
                if((string)window.list.Controls[0].Tag!=window.data.Tasks[1].Id || (string)window.list.Controls[1].Tag!=window.data.Tasks[0].Id) throw new Exception("Completed task did not move below active task");
                window.data.Tasks[0].Done=false; window.RefreshTasks();
                if((string)window.list.Controls[0].Tag!=window.data.Tasks[0].Id) throw new Exception("Unchecked task did not return to active group");
                window.data.Tasks[1].Important=false; window.data.Tasks[1].Urgent=true; window.RefreshTasks();
                if((string)window.quadrants[0].Controls[0].Tag!=window.data.Tasks[0].Id || (string)window.quadrants[2].Controls[0].Tag!=window.data.Tasks[1].Id || (string)window.quadrants[3].Controls[0].Tag!=window.data.Tasks[2].Id) throw new Exception("Matrix routing");
                int leftEdge=window.Left;
                window.SwitchView(true); if(window.ClientSize.Height!=724 || window.ClientSize.Width!=600 || window.Left!=leftEdge) throw new Exception("Matrix must expand to the right");
                window.SwitchView(false); if(window.ClientSize.Height!=648 || window.ClientSize.Width!=410 || window.input.Width!=290) throw new Exception("List layout");
                if(Quadrant(new TaskItem())!=-1) throw new Exception("New tasks must not be assigned automatically");
                window.saveOverride=delegate { return true; };
                int before=window.data.Tasks.Count;
                var chosen=window.data.Tasks[0]; window.AssignMatrix(chosen,3);
                if(window.data.Tasks.Count!=before || Quadrant(chosen)!=3) throw new Exception("Assignment duplicated task");
                window.AssignMatrix(chosen,-1);
                if(Quadrant(chosen)!=-1 || window.list.Controls.Count!=before) throw new Exception("Remove from matrix must preserve task");
                window.saveOverride=delegate { return false; }; window.AssignMatrix(chosen,2);
                if(window.data.Tasks.Count!=before || Quadrant(chosen)!=-1) throw new Exception("Failed save must roll back matrix changes");
                using(var draft=new TextBox { Text="  Новое дело  " }) {
                    window.saveOverride=delegate { return true; }; window.AddMatrixTask(1,draft);
                    if(window.data.Tasks.Count!=before+1 || Quadrant(window.data.Tasks[before])!=1 || window.data.Tasks[before].Text!="Новое дело" || draft.Text!="") throw new Exception("Matrix input must add to selected quadrant");
                    window.AddMatrixTask(1,draft);
                    if(window.data.Tasks.Count!=before+1) throw new Exception("Empty matrix input must be ignored");
                    window.saveOverride=delegate { return false; }; draft.Text="Черновик"; window.AddMatrixTask(2,draft);
                    if(window.data.Tasks.Count!=before+1 || draft.Text!="Черновик") throw new Exception("Failed save must preserve matrix draft");
                }
            }
            Directory.CreateDirectory(folder); var d=new TaskData(); DateTime today=new DateTime(2026,10,14);
            d.Tasks.Add(new TaskItem { Text="Остаётся",Important=false,Urgent=true,InMatrix=true }); d.Tasks.Add(new TaskItem { Text="Вчера",Done=true,CompletedAt=today.AddDays(-1).AddHours(12) });
            if(Storage.Clean(d,today.AddHours(2).AddMinutes(59))!=0) throw new Exception("Before boundary");
            if(Storage.Clean(d,today.AddHours(3))!=1 || d.Tasks.Count!=1) throw new Exception("Boundary cleanup");
            d.Tasks.Add(new TaskItem { Text="Сегодня",Done=true,CompletedAt=today.AddHours(4) });
            if(Storage.Clean(d,today.AddHours(22))!=0) throw new Exception("Same-day completion");
            string path=Path.Combine(folder,"test.xml"); Storage.Write(path,d); var loaded=Storage.Read(path);
            if(loaded.Tasks.Count!=2 || loaded.Tasks[0].Text!="Остаётся") throw new Exception("Persistence");
            if(loaded.Tasks[0].Important || !loaded.Tasks[0].Urgent || !loaded.Tasks[0].InMatrix) throw new Exception("Priority persistence");
            using(var legacy=new StringReader("<TaskData><Tasks><TaskItem><Text>Старое дело</Text></TaskItem></Tasks></TaskData>")) {
                var migrated=(TaskData)new XmlSerializer(typeof(TaskData)).Deserialize(legacy);
                if(migrated.Tasks.Count!=1 || Quadrant(migrated.Tasks[0])!=-1) throw new Exception("Legacy tasks must remain in the list only");
            }
            Storage.Write(path,loaded); if(!File.Exists(path+".bak")) throw new Exception("Backup");
            if(Storage.Clean(loaded,today.AddDays(3).AddHours(10))!=1 || loaded.Tasks[0].Done) throw new Exception("Offline rollover");
            loaded.Tasks.Add(new TaskItem { Text="Отмена отметки",Done=false,CompletedAt=today });
            if(Storage.Clean(loaded,today.AddDays(5))!=0) throw new Exception("Unchecked task");
            File.WriteAllText(Path.Combine(folder,"test-result.txt"),"PASS: cleanup, persistence, completed-task grouping, matrix routing, priority persistence and legacy migration"); return 0;
        } catch(Exception ex) { File.WriteAllText(Path.Combine(folder,"test-result.txt"),ex.ToString()); return 1; }
    }
}
class RoundedPanel : Panel {
    public static GraphicsPath Shape(int width,int height,int radius) {
        float x=1f,y=1f,w=width-2f,h=height-2f,d=Math.Min(radius*2f,Math.Min(w,h)); var p=new GraphicsPath();
        p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90); p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90); p.CloseFigure(); return p;
    }
    public RoundedPanel() { DoubleBuffered=true; }
    public void PaintGlass(Graphics g) {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        var form=FindForm() as DayTasks; bool light=form!=null && form.LightGlass;
        using(var path=Shape(Width,Height,16)) {
            using(var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(light ? 35 : 12,255,255,255),Color.FromArgb(light ? 12 : 18,12,24,30),70f)) g.FillPath(fill,path);
            using(var edge=new Pen(Color.FromArgb(light ? 75 : 45,255,255,255),1f)) g.DrawPath(edge,path);
        }
    }
    protected override void OnPaintBackground(PaintEventArgs e) {
        var form=FindForm() as DayTasks;
        if(form!=null && form.IsGlass) { GlassPaint.Background(this,e.Graphics); PaintGlass(e.Graphics); }
        else {
            e.Graphics.Clear(Parent==null ? BackColor : Parent.BackColor); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=Shape(Width,Height,14)) using(var brush=new SolidBrush(BackColor)) e.Graphics.FillPath(brush,path);
        }
    }
}
static class GlassPaint {
    public static void Background(Control control,Graphics g) {
        var form=control.FindForm() as DayTasks;
        if(form==null || !form.IsGlass) { g.Clear(control.Parent==null ? control.BackColor : control.Parent.BackColor); return; }
        WallpaperBackdrop.Draw(control,g);
        Point origin=control.PointToScreen(Point.Empty),root=form.PointToScreen(Point.Empty);
        Rectangle bounds=new Rectangle(root.X-origin.X,root.Y-origin.Y,form.Width,form.Height);
        using(var tint=new SolidBrush(Color.FromArgb(form.LightGlass ? 90 : 55,form.GlassTint))) g.FillRectangle(tint,bounds);
        using(var sheen=new LinearGradientBrush(bounds,Color.FromArgb(10,255,255,255),Color.FromArgb(12,0,0,0),80f)) g.FillRectangle(sheen,bounds);
        var state=g.Save();
        var parent=control.Parent as RoundedPanel;
        if(parent!=null) { state=g.Save(); g.TranslateTransform(-control.Left,-control.Top); parent.PaintGlass(g); g.Restore(state); }
    }
}
class BackdropPanel : Panel {
    public BackdropPanel() { DoubleBuffered=true; }
    protected override void OnPaintBackground(PaintEventArgs e) { GlassPaint.Background(this,e.Graphics); }
}
class BackdropFlow : FlowLayoutPanel {
    public BackdropFlow() { DoubleBuffered=true; }
    protected override void OnPaintBackground(PaintEventArgs e) { GlassPaint.Background(this,e.Graphics); }
}
class SoftButton : Button {
    public bool Emphasized;
    public SoftButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); }
    protected override void OnPaint(PaintEventArgs e) {
        GlassPaint.Background(this,e.Graphics); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=RoundedPanel.Shape(Width,Height,10)) {
            var form=FindForm() as DayTasks;
            if(form!=null && form.IsGlass) {
                Color top=Color.FromArgb(Text=="+" ? 115 : Emphasized ? 90 : 35,255,255,255),bottom=Color.FromArgb(35,20,35,45);
                using(var fill=new LinearGradientBrush(ClientRectangle,top,bottom,90f)) e.Graphics.FillPath(fill,path);
                using(var border=new Pen(Color.FromArgb(75,255,255,255),1)) e.Graphics.DrawPath(border,path);
            } else { using(var brush=new SolidBrush(BackColor)) e.Graphics.FillPath(brush,path); }
        }
        if(Text=="×" || Text=="+") {
            float cx=Width/2f,cy=Height/2f,half=Text=="+" ? 6f : 3.5f;
            using(var pen=new Pen(ForeColor,Text=="+" ? 1.8f : 1.4f)) {
                pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round;
                if(Text=="+") { e.Graphics.DrawLine(pen,cx-half,cy,cx+half,cy); e.Graphics.DrawLine(pen,cx,cy-half,cx,cy+half); }
                else { e.Graphics.DrawLine(pen,cx-half,cy-half,cx+half,cy+half); e.Graphics.DrawLine(pen,cx-half,cy+half,cx+half,cy-half); }
            }
        } else TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}
class TaskCheck : Button {
    readonly bool done; readonly Color card,muted,accent,mark;
    public TaskCheck(bool done,Color card,Color muted,Color accent,Color mark) {
        this.done=done; this.card=card; this.muted=muted; this.accent=accent; this.mark=mark;
        Size=new Size(28,28); BackColor=card; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; Cursor=Cursors.Hand;
        AccessibleName=done ? "Снять отметку выполнения" : "Отметить выполненным";
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnPaint(PaintEventArgs e) {
        var form=FindForm() as DayTasks; if(form!=null && form.IsGlass) GlassPaint.Background(this,e.Graphics); else e.Graphics.Clear(card); e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        if(done) {
            using(var brush=new SolidBrush(accent)) e.Graphics.FillEllipse(brush,2,2,24,24);
            using(var pen=new Pen(mark,2.5f)) { pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round; e.Graphics.DrawLines(pen,new Point[] { new Point(8,14),new Point(12,18),new Point(20,9) }); }
        } else { using(var pen=new Pen(muted,1.5f)) e.Graphics.DrawEllipse(pen,3,3,22,22); }
        if(Focused) ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(0,0,27,27),muted,card);
    }
}
