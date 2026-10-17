using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    static void VerifyConfirmationAreaAndPlacement(Action<bool,string> check)
    {
        var selection=new GestureSelection();
        foreach(int count in new[]{1,3,8,16})for(int index=0;index<count;index++)
        {
            double direction=-Math.PI/2+index*2*Math.PI/count;
            selection.Begin(count,209,58);selection.Update(140*Math.Cos(direction),140*Math.Sin(direction),0);selection.Update(140*Math.Cos(direction),140*Math.Sin(direction),150);
            foreach(double offset in new[]{-.98,-.6,0,.6,.98})foreach(double radius in new[]{209.25,219,296.75,450})
            {
                double angle=direction+offset*Math.PI/count;
                selection.Update(radius*Math.Cos(angle),radius*Math.Sin(angle),160);
                check(selection.Confirmed,$"{count} 项 / {index} / 角偏移 {offset} / 半径 {radius} 整片外拉范围有效");
            }
            selection.Update(140*Math.Cos(direction),140*Math.Sin(direction),170);
            check(!selection.Confirmed && selection.Release(true).Length==0,$"{count} 项 / {index} 从范围返回圈内撤销确认");
        }
        foreach(double dpi in new[]{1,1.25,1.5,1.75,2})foreach(int diameter in new[]{300,420,680})
        foreach(var point in new[]{(2,2),(1918,1078),(-1918,1078),(0,-1078)})
        {
            var bounds=RadialWindow.AnchoredBounds(point.Item1,point.Item2,diameter+212,dpi);
            check(bounds.Left+bounds.Pixels/2d==point.Item1 && bounds.Top+bounds.Pixels/2d==point.Item2,$"{dpi} DPI / {diameter} DIP / {point} 物理中心不偏移");
            check(bounds.Pixels/dpi>=diameter+212 && bounds.Pixels/dpi<diameter+212+2/dpi,$"{dpi} DPI / {diameter} DIP / {point} 边缘不缩小轮盘");
        }
        foreach(var palette in Themes.Options)
        {
            const int size=632;var settings=Settings.Defaults();settings.Theme=palette.Id;
            var surface=new RadialSurface{Width=size,Height=size};surface.Configure(settings);
            surface.Measure(new Size(size,size));surface.Arrange(new Rect(0,0,size,size));surface.UpdateLayout();
            surface.Gesture.Begin(8,surface.OuterRadius,58);surface.Gesture.Update(140,0,0);surface.Gesture.Update(140,0,150);surface.Gesture.Update(211,0,160);surface.InvalidateVisual();surface.UpdateLayout();
            var image=new RenderTargetBitmap(size*2,size*2,192,192,PixelFormats.Pbgra32);image.Render(surface);var pixels=new byte[size*size*16];int stride=size*8;image.CopyPixels(pixels,stride,0);
            byte Alpha(double r){int x=(int)Math.Round((316+r*Math.Cos(.22))*2),y=(int)Math.Round((316+r*Math.Sin(.22))*2);return pixels[y*stride+x*4+3];}
            bool smooth=true;for(double r=surface.OuterRadius+14;r<surface.OuterRadius+76;r+=.5)if(Alpha(r+.5)>Alpha(r)+1)smooth=false;
            check(smooth,$"{palette.Id} 外拉渐变连续递减，无虚线范围边框或按钮描边");
            bool transparent=true;for(int i=0;i<size*2;i++)foreach(int offset in new[]{i*4,(size*2-1)*stride+i*4,i*stride,i*stride+(size*2-1)*4})if(pixels[offset+3]!=0)transparent=false;
            check(transparent,$"{palette.Id} 手势画布四边透明，不绘制矩形范围框");
        }
    }
    static async Task VerifyEdgeGestures(AppController controller,string folder,Action<bool,string> check)
    {
        var original=controller.Config.Items;var launched=new List<string>();controller.TestLaunch=item=>launched.Add(item.Name);
        controller.Config.Items=Settings.Defaults().Items;controller.Apply();
        var monitor=Native.MonitorFromPoint(new Native.POINT(400,400),2);var info=new Native.MONITORINFO{Size=Marshal.SizeOf<Native.MONITORINFO>()};
        if(!Native.GetMonitorInfo(monitor,ref info))throw new Exception("无法读取边界验证显示器");
        var work=info.Work;var bounds=info.Monitor;int cx=(work.Left+work.Right)/2,cy=(work.Top+work.Bottom)/2;
        try
        {
            foreach(var point in new[]{(X:cx,Y:work.Bottom-2),(X:cx,Y:bounds.Bottom-2),(X:work.Left+2,Y:cy),(X:work.Right-2,Y:cy),(X:cx,Y:bounds.Top+2)})
            {
                int selectedIndex=point.X==work.Left+2?2:point.X==work.Right-2?6:point.Y==bounds.Top+2?4:0;
                double direction=-Math.PI/2+selectedIndex*Math.PI/4;
                controller.TestModifierDown();controller.TestPointer(0x201,point.X,point.Y);controller.TestPointer(0x200,point.X+(int)Math.Round(40*Math.Cos(direction)),point.Y+(int)Math.Round(40*Math.Sin(direction)));await Task.Delay(170);
                var wheel=controller.TestWheel;wheel.UpdateLayout();var place=wheel.Placement;
                check(place.X==point.X && place.Y==point.Y,$"Edge{point}LogicalOriginMatchesMouseDown");
                var visualCenter=wheel.Surface.PointToScreen(new Point(wheel.Surface.ActualWidth/2,wheel.Surface.ActualHeight/2));
                check(Math.Abs(visualCenter.X-point.X)<=1 && Math.Abs(visualCenter.Y-point.Y)<=1,$"Edge{point}ActualWpfCentreMatchesMouseDown");
                var hwnd=new WindowInteropHelper(wheel).Handle;check(Native.GetWindowRect(hwnd,out var rect) && Math.Abs((rect.Left+rect.Right)/2d-point.X)<=1 && Math.Abs((rect.Top+rect.Bottom)/2d-point.Y)<=1,$"Edge{point}NativeBoundsRemainCentred");
                check((Native.GetWindowLongPtr(hwnd,-16).ToInt64()&0x00C40000)==0 && wheel.BorderThickness==new Thickness(0),$"Edge{point}NativeWindowHasNoRectangularFrame");
                controller.TestPointer(0x200,(int)Math.Round(point.X+140*Math.Cos(direction)*place.Scale),(int)Math.Round(point.Y+140*Math.Sin(direction)*place.Scale));await Task.Delay(230);
                double angle=direction+Math.PI/8*.85,radius=wheel.Surface.OuterRadius+2;
                int x=(int)Math.Round(point.X+radius*Math.Cos(angle)*place.Scale),y=(int)Math.Round(point.Y+radius*Math.Sin(angle)*place.Scale);
                controller.TestPointer(0x200,x,y);await Task.Delay(40);
                check(wheel.Selection.Confirmed,$"Edge{point}NearEdgeOfWholeConfirmationAreaArms");
                if(point.Y==work.Bottom-2)SaveVisual(wheel,Path.Combine(folder,"任务栏边缘-1.3.3.png"));
                controller.TestPointer(0x202,x,y);await Task.Delay(110);controller.TestModifierUp();
                check(launched.SequenceEqual(new[]{controller.Config.Items[selectedIndex].Name}),$"Edge{point}ConfirmedReleaseDispatchesExactlyOnce");launched.Clear();
            }
        }
        finally{controller.Recover();controller.TestModifierUp();controller.Config.Items=original;controller.Apply();controller.TestLaunch=null;}
    }
}
