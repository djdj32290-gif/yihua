using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Windows.Threading;

namespace OrbitLauncher;
internal sealed class RadialWindow : Window
{
    readonly RadialSurface surface=new();
    readonly DispatcherTimer dwellTimer;
    double centerX,centerY,scale=1;
    int generation;
    bool tracking;
    internal bool DwellTimerRunning=>dwellTimer.IsEnabled;
    internal GestureSelection Selection=>surface.Gesture;
    internal RadialSurface Surface=>surface;
    internal (double X,double Y,double Scale) Placement=>(centerX,centerY,scale);
    public RadialWindow()
    {
        Title="一划 · 轮盘"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
        AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=false; ShowActivated=false;
        BorderThickness=new Thickness(0);FocusVisualStyle=null;
        Topmost=true; Focusable=false; Content=surface;
        dwellTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(25)};
        dwellTimer.Tick+=(_,_)=> {if(tracking)UpdateSelection(surface.Gesture.X,surface.Gesture.Y);else dwellTimer.Stop();};
        Closed+=(_,_)=>dwellTimer.Stop();
        SourceInitialized+=(_,_)=> { var h=new WindowInteropHelper(this).Handle; Native.SetWindowLongPtr(h,-20,new IntPtr(Native.GetWindowLongPtr(h,-20).ToInt64()|0x08000000|0x80)); };
    }
    public void Prepare(Settings settings) => surface.Configure(settings.Copy());
    public void OpenAt(int x,int y,Settings settings)
    {
        generation++; BeginAnimation(OpacityProperty,null); Opacity=1;
        scale=Native.ScreenAt(x,y).Scale;
        double size=settings.Diameter+2*(GestureSelection.Extension+18);
        centerX=x;centerY=y;
        Width=Height=size;
        surface.Configure(settings.Copy());surface.Width=surface.Height=size;
        surface.Gesture.Begin(settings.Items.Count,surface.OuterRadius,settings.CancelRadius);
        tracking=true;
        surface.Selected=-1; surface.InvalidateVisual();
        Show();
        PlaceAtOrigin(size);
        // The actual HWND DPI is authoritative when the window crosses a monitor.
        double actualScale=VisualTreeHelper.GetDpi(this).DpiScaleX;
        if(Math.Abs(actualScale-scale)>.001){scale=actualScale;PlaceAtOrigin(size);}
        surface.InvalidateMeasure();
        BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(95)));
    }
    internal static (int Left,int Top,int Pixels) AnchoredBounds(int x,int y,double size,double dpi)
    {
        // An even pixel extent keeps the physical centre exactly on the origin,
        // including fractional scaling and negative coordinates on secondary monitors.
        int half=(int)Math.Ceiling(size*dpi/2);
        return(x-half,y-half,half*2);
    }
    void PlaceAtOrigin(double size)
    {
        var bounds=AnchoredBounds((int)centerX,(int)centerY,size,scale);
        double dipSize=bounds.Pixels/scale;Width=Height=dipSize;surface.Width=surface.Height=dipSize;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),bounds.Left,bounds.Top,bounds.Pixels,bounds.Pixels,0x10|0x40);
    }
    public void Track(int x,int y)
    {if(tracking)UpdateSelection((x-centerX)/scale,(y-centerY)/scale);}
    void UpdateSelection(double x,double y)
    {
        bool changed=surface.Gesture.Update(x,y,Environment.TickCount64);surface.Selected=surface.Gesture.Hovered;
        if(changed)surface.InvalidateVisual();
        if(surface.Gesture.NeedsDwell)dwellTimer.Start();else dwellTimer.Stop();
    }
    internal int[] Finish(bool commit)=>surface.Gesture.Release(commit);
    public void Dismiss(bool immediate=false)
    {
        tracking=false;dwellTimer.Stop();surface.Gesture.Clear();surface.Selected=-1;surface.InvalidateVisual();
        if(!IsVisible) return;
        int token=++generation;
        if(immediate) { BeginAnimation(OpacityProperty,null); Hide(); return; }
        var anim=new DoubleAnimation(0,TimeSpan.FromMilliseconds(75)); anim.Completed+=(_,_)=> { if(token==generation) Hide(); }; BeginAnimation(OpacityProperty,anim);
    }
}
public sealed class RadialSurface : FrameworkElement
{
    public bool EditorMode { get; set; }
    internal bool DeleteMode {get;private set;}
    internal HashSet<int> DeleteSelection {get;}=new();
    internal event Action? DeleteSelectionChanged;
    internal GestureSelection Gesture {get;}=new();
    internal double OuterRadius=>EditorMode?ActualWidth/2-17:Math.Max(Settings.CancelRadius+24,Math.Min(Settings.Diameter/2d-1,(double.IsNaN(Width)?ActualWidth:Width)/2-GestureSelection.Extension-18));
    internal void SetDeleteMode(bool value){CancelEditorDrag();DeleteMode=value;DeleteSelection.Clear();DeleteSelectionChanged?.Invoke();InvalidateVisual();}
    internal void ToggleDelete(int index){if(index<0 || index>=Settings.Items.Count)return;if(!DeleteSelection.Add(index))DeleteSelection.Remove(index);DeleteSelectionChanged?.Invoke();InvalidateVisual();}
    public event Action<int>? EditRequested;
    public event Action<int,int>? SwapRequested;
    bool centerHovered;
    bool editorPressed,editorDragging;
    int dragSource=-2,dragTarget=-1;
    Point pressPoint,dragPoint;
    public RadialSurface()
    {
        Focusable=true;FocusVisualStyle=null;
        MouseLeftButtonDown+=(_,e)=> {if(!EditorMode)return;Focus();EditorPointerDown(e.GetPosition(this));if(editorPressed)CaptureMouse();e.Handled=true;};
        MouseMove+=(_,e)=> {if(EditorMode)EditorPointerMove(e.GetPosition(this));};
        MouseLeave+=(_,_)=> {if(EditorMode && !editorPressed){Selected=-1;centerHovered=false;InvalidateVisual();}};
        MouseLeftButtonUp+=(_,e)=> {if(!EditorMode)return;EditorPointerUp(e.GetPosition(this));e.Handled=true;};
        LostMouseCapture+=(_,_)=> {if(EditorMode)CancelEditorDrag();};
        PreviewKeyDown+=(_,e)=> {if(EditorMode && e.Key==Key.Escape && editorPressed){CancelEditorDrag();e.Handled=true;}};
        KeyDown+=(_,e)=> {if(!EditorMode)return;if(e.Key is Key.Left or Key.Up){Selected=Settings.Items.Count==0?-1:(Selected-1+Settings.Items.Count)%Settings.Items.Count;}else if(e.Key is Key.Right or Key.Down){Selected=Settings.Items.Count==0?-1:(Selected+1)%Settings.Items.Count;}else if(e.Key==Key.Enter || e.Key==Key.Space && DeleteMode){if(DeleteMode)ToggleDelete(Selected);else EditRequested?.Invoke(Selected);e.Handled=true;return;}else if(e.Key is Key.Add or Key.OemPlus){if(!DeleteMode)EditRequested?.Invoke(-1);e.Handled=true;return;}else return;InvalidateVisual();e.Handled=true;};
    }
    internal int EditorHit(Point point)
    {
        double x=point.X-ActualWidth/2,y=point.Y-ActualHeight/2;
        if(x*x+y*y>Math.Pow(ActualWidth/2-17,2))return -2;
        return RadialMath.Select(x,y,Settings.CancelRadius,Settings.Items.Count);
    }
    internal void ActivateEditor(Point point){if(!EditorMode)return;int hit=EditorHit(point);if(DeleteMode)ToggleDelete(hit);else if(hit>=-1)EditRequested?.Invoke(hit);}
    internal void EditorPointerDown(Point point)
    {CancelEditorDrag();dragSource=EditorHit(point);editorPressed=dragSource>=-1;pressPoint=dragPoint=point;}
    internal void EditorPointerMove(Point point)
    {
        int hit=EditorHit(point);dragPoint=point;Selected=hit>=0?hit:-1;centerHovered=hit==-1;
        if(!DeleteMode && editorPressed && dragSource>=0 && (point-pressPoint).Length>=6)editorDragging=true;
        dragTarget=editorDragging && hit>=0 && hit!=dragSource?hit:-1;
        Cursor=editorDragging?(dragTarget>=0?Cursors.SizeAll:Cursors.No):hit>=-1?Cursors.Hand:Cursors.Arrow;InvalidateVisual();
    }
    internal void EditorPointerUp(Point point)
    {
        if(!editorPressed)return;
        EditorPointerMove(point);int source=dragSource,target=dragTarget,hit=EditorHit(point);bool moved=editorDragging;
        CancelEditorDrag();
        if(moved){if(source>=0 && target>=0)SwapRequested?.Invoke(source,target);}
        else if(hit==source && hit>=-1){if(DeleteMode)ToggleDelete(hit);else EditRequested?.Invoke(hit);}
    }
    internal void CancelEditorDrag()
    {editorPressed=editorDragging=false;dragSource=-2;dragTarget=-1;if(IsMouseCaptured)ReleaseMouseCapture();Cursor=Cursors.Arrow;InvalidateVisual();}
    public Settings Settings { get; private set; } = new();
    List<ImageSource> icons=new();
    List<ItemAppearance.Palette> palettes=new();
    public int Selected { get; set; }=-1;
    static readonly ConcurrentDictionary<uint,SolidColorBrush> brushes=new();
    static SolidColorBrush Brush(byte r,byte g,byte b,byte a=255)=>brushes.GetOrAdd(((uint)a<<24)|((uint)r<<16)|((uint)g<<8)|b,static key=> {var v=new SolidColorBrush(Color.FromArgb((byte)(key>>24),(byte)(key>>16),(byte)(key>>8),(byte)key));v.Freeze();return v;});
    ThemePalette skin=Themes.Get("Obsidian");
    Brush Text=>skin.Brush("WheelText");
    Brush Muted=>skin.Brush("WheelMuted");
    Brush Accent=>skin.Brush("WheelAccent");
    static readonly Typeface fontFace=new("Microsoft YaHei UI");
    static readonly CultureInfo culture=CultureInfo.GetCultureInfo("zh-CN");
    readonly Dictionary<(Point,double,double,double,double),Geometry> geometryCache=new();
    readonly Dictionary<(string,double,Color,double,int,double),FormattedText> textCache=new();
    public void Configure(Settings settings) { CancelEditorDrag();geometryCache.Clear();textCache.Clear();Settings=settings;skin=Themes.Get(settings.Theme); icons=settings.Items.Select(i=>Icons.For(i.Target)).ToList();palettes=settings.Items.Select(i=>ItemAppearance.For(ItemAppearance.Classify(i.Target),settings.Theme)).ToList(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double size=ActualWidth, c=size/2, outer=OuterRadius, inner=Settings.CancelRadius+10;
        var cp=new Point(c,c);
        dc.DrawEllipse(Brush(0,0,0,35),null,new Point(c,c+5),outer+9,outer+9);
        dc.DrawEllipse(skin.Brush("WheelBackground"),new Pen(skin.Brush("WheelBorder"),1),cp,outer,outer);
        int count=Settings.Items.Count;
        for(int i=0;i<count;i++)
        {
            double angle=-Math.PI/2+i*Math.PI*2/count, half=Math.PI/count;
            var geo=Wedge(cp,inner,outer-5,angle-half,angle+half);
            var palette=palettes[i];
            bool marked=EditorMode?DeleteSelection.Contains(i):Gesture.Contains(i);
            var ink=DeleteMode && marked?skin.Brush("WheelDelete"):palette.Accent;
            dc.DrawGeometry(marked || i==Selected?palette.Hover:palette.Background,new Pen(marked || i==Selected?ink:palette.Border,marked?2:i==Selected?1.4:.6),geo);
            if(DeleteMode && marked)dc.DrawGeometry(Brush(216,61,90,65),null,geo);
            if(EditorMode && editorDragging && (i==dragSource || i==dragTarget))dc.DrawGeometry(i==dragTarget?Brush(83,151,219,90):null,new Pen(Accent,i==dragTarget?2:1){DashStyle=i==dragSource?DashStyles.Dash:DashStyles.Solid},geo);
            double radius=inner+(outer-inner)*.54;
            double ix=c+Math.Cos(angle)*radius, iy=c+Math.Sin(angle)*radius;
            double iconSize=EditorMode?(count>12?32:40):(count>12?28:34);
            if(i<icons.Count) dc.DrawImage(icons[i],new Rect(ix-iconSize/2,iy-iconSize/2-10,iconSize,iconSize));
            DrawText(dc,Settings.Items[i].Name,new Point(ix,iy+iconSize/2-3),EditorMode?(count>12?14:16):(count>12?10.5:12),i==Selected?Text:skin.Brush("WheelLabel"),Math.Max(28,Math.Min(108,Math.PI*2*radius/count-8)),1);
            if(EditorMode && DeleteMode)
            {
                var badge=At(cp,outer-24,angle);dc.DrawEllipse(marked?skin.Brush("WheelDelete"):skin.Brush("Button"),new Pen(marked?skin.Brush("DangerText"):Muted,1.2),badge,10,10);
                if(marked){var p=new Pen(Brushes.White,1.7);dc.DrawLine(p,new Point(badge.X-4,badge.Y),new Point(badge.X-1,badge.Y+3));dc.DrawLine(p,new Point(badge.X-1,badge.Y+3),new Point(badge.X+5,badge.Y-4));}
            }
        }
        if(count>0)
        {
            // A full annulus underneath and slightly overlapping colour spans
            // prevent antialiasing seams at every sector boundary.
            dc.DrawEllipse(null,new Pen(palettes[0].Accent,4),cp,outer-7,outer-7);
            for(int i=0;i<count;i++)
            {
                double angle=-Math.PI/2+i*Math.PI*2/count,half=Math.PI/count;
                dc.DrawGeometry(palettes[i].Accent,null,Wedge(cp,outer-9,outer-5,angle-half-.002,angle+half+.002));
            }
        }
        if(!EditorMode)DrawGesture(dc,cp,outer);
        bool cancel=EditorMode?centerHovered:!Gesture.Confirmed;
        dc.DrawEllipse(skin.Brush("WheelCenter"),new Pen(cancel?Accent:skin.Brush("WheelBorder"),cancel?1.5:1),cp,Settings.CancelRadius,Settings.CancelRadius);
        if(EditorMode)
        {
            if(DeleteMode){DrawText(dc,$"已选 {DeleteSelection.Count}",new Point(c,c-15),15,skin.Brush("DangerText"),Settings.CancelRadius*1.8,1);DrawText(dc,"点击勾选",new Point(c,c+10),10,Muted,Settings.CancelRadius*1.8,1);return;}
            var ink=Settings.Items.Count<16?Accent:Muted;
            var pen=new Pen(ink,2);dc.DrawLine(pen,new Point(c-8,c-14),new Point(c+8,c-14));dc.DrawLine(pen,new Point(c,c-22),new Point(c,c-6));
            DrawText(dc,Settings.Items.Count<16?"添加选项":"数量已满",new Point(c,c+4),12,ink,Settings.CancelRadius*1.8,1);
        }
        else if(Gesture.Stops.Count>0)
        {
            DrawText(dc,$"已选 {Gesture.Stops.Count} 项",new Point(c,c-22),14,Text,Settings.CancelRadius*1.8,1);
            DrawText(dc,Gesture.Confirmed?"松开全部打开":"拉出轮盘以确认",new Point(c,c+2),11,Gesture.Confirmed?skin.Brush("AccentText"):Muted,Settings.CancelRadius*1.9,1);
            DrawText(dc,Gesture.Confirmed?"释放前仍可返回":"圈内松开全部取消",new Point(c,c+23),8.5,Muted,Settings.CancelRadius*1.9,1);
        }
        else if(cancel && Selected<0)
        {
            var p=new Pen(Muted,1.5); dc.DrawLine(p,new Point(c-5,c-19),new Point(c+5,c-9)); dc.DrawLine(p,new Point(c+5,c-19),new Point(c-5,c-9));
            DrawText(dc,"松开取消",new Point(c,c+3),13,Text,Settings.CancelRadius*1.8,1);
            DrawText(dc,"ESC",new Point(c,c+23),9,Muted,80,1);
        }
        else
        {
            DrawText(dc,Settings.Items[Selected].Name,new Point(c,c-14),13,Text,Settings.CancelRadius*1.75,2);
            DrawText(dc,"短暂停留以选择",new Point(c,c+25),10,Accent,100,1);
        }
        if(EditorMode && editorDragging && dragSource>=0 && dragSource<icons.Count)
        {
            var at=new Point(dragPoint.X+27,dragPoint.Y-28);
            dc.DrawRoundedRectangle(skin.Brush("Popup"),new Pen(Accent,1),new Rect(at.X-25,at.Y-25,50,50),12,12);
            dc.DrawImage(icons[dragSource],new Rect(at.X-17,at.Y-17,34,34));
            DrawText(dc,dragTarget>=0?"松开交换":"拖到另一个选项",new Point(at.X,at.Y+31),10,Accent,110,1);
        }
    }
    void DrawGesture(DrawingContext dc,Point center,double outer)
    {
        int armed=Gesture.Armed,count=Settings.Items.Count;
        if(armed>=0 && armed<count)
        {
            double a=-Math.PI/2+armed*2*Math.PI/count,half=Math.PI/count;
            var color=palettes[armed].Accent.Color;
            var gradient=new RadialGradientBrush();
            byte opacity=Gesture.Confirmed?(byte)180:(byte)120;
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(opacity,color.R,color.G,color.B),0));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(opacity,color.R,color.G,color.B),outer/(outer+GestureSelection.Extension)));
            gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),1));
            dc.PushClip(Wedge(center,outer,outer+GestureSelection.Extension,a-half,a+half));
            dc.DrawEllipse(gradient,null,center,outer+GestureSelection.Extension,outer+GestureSelection.Extension);
            dc.Pop();
        }
        if(Gesture.Stops.Count==0 && Gesture.Candidate<0)return;
        var path=new StreamGeometry();
        // Keep the live tail inside the canvas even when the pointer travels far outside it.
        var delta=new Vector(Gesture.X,Gesture.Y);if(delta.Length>outer+GestureSelection.Extension-4){delta.Normalize();delta*=outer+GestureSelection.Extension-4;}
        using(var ctx=path.Open()){ctx.BeginFigure(center,false,false);foreach(var stop in Gesture.Stops)ctx.LineTo(new Point(center.X+stop.X,center.Y+stop.Y),true,true);ctx.LineTo(center+delta,true,true);}
        var pathColor=skin.Brush("WheelPath").Color;
        var glow=Brush(pathColor.R,pathColor.G,pathColor.B,45);var line=Brush(pathColor.R,pathColor.G,pathColor.B,230);
        dc.DrawGeometry(null,new Pen(glow,9){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},path);
        dc.DrawGeometry(null,new Pen(line,2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round},path);
        for(int i=0;i<Gesture.Stops.Count;i++)
        {
            var stop=Gesture.Stops[i];var p=new Point(center.X+stop.X,center.Y+stop.Y);
            dc.DrawEllipse(skin.Brush("WheelNode"),new Pen(line,1.7),p,10,10);
            DrawText(dc,(i+1).ToString(),new Point(p.X,p.Y-6.5),10,skin.Brush("WheelNodeText"),20,1);
        }
        if(Gesture.Candidate>=0 && Gesture.Progress<1)
        {
            var p=center+delta;dc.DrawEllipse(Brush(75,237,203,45),new Pen(line,1),p,6+Gesture.Progress*5,6+Gesture.Progress*5);
        }
    }
    void DrawText(DrawingContext dc,string value,Point center,double font,Brush brush,double width,int lines)
    {
        double dpi=VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var key=(value,font,((SolidColorBrush)brush).Color,width,lines,dpi);
        if(!textCache.TryGetValue(key,out var text))
        {
            if(textCache.Count>=256)textCache.Clear();
            text=new FormattedText(value,culture,FlowDirection.LeftToRight,fontFace,font,brush,dpi) { MaxTextWidth=Math.Max(1,width),MaxLineCount=lines,Trimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center };
            textCache.Add(key,text);
        }
        dc.DrawText(text,new Point(center.X-width/2,center.Y));
    }
    static Point At(Point c,double r,double a)=>new(c.X+r*Math.Cos(a),c.Y+r*Math.Sin(a));
    Geometry Wedge(Point c,double inner,double outer,double a,double b)
    {
        var key=(c,inner,outer,a,b);
        if(!geometryCache.TryGetValue(key,out var geometry))
        {if(geometryCache.Count>=128)geometryCache.Clear();geometry=CreateWedge(c,inner,outer,a,b);geometryCache.Add(key,geometry);}
        return geometry;
    }
    static Geometry CreateWedge(Point c,double inner,double outer,double a,double b)
    {
        if(b-a>=Math.PI*2-.00001){var ring=new GeometryGroup{FillRule=FillRule.EvenOdd};ring.Children.Add(new EllipseGeometry(c,outer,outer));ring.Children.Add(new EllipseGeometry(c,inner,inner));ring.Freeze();return ring;}
        var g=new StreamGeometry(); using(var x=g.Open()) { x.BeginFigure(At(c,inner,a),true,true); x.LineTo(At(c,outer,a),true,false); x.ArcTo(At(c,outer,b),new Size(outer,outer),0,b-a>Math.PI,SweepDirection.Clockwise,true,false); x.LineTo(At(c,inner,b),true,false); x.ArcTo(At(c,inner,a),new Size(inner,inner),0,b-a>Math.PI,SweepDirection.Counterclockwise,true,false); } g.Freeze(); return g;
    }
}
