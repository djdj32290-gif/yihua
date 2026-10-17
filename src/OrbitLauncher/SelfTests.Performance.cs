using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    // Runs before hooks, configuration, tray or target launching are initialized.
    public static int BenchmarkRender(string[] args)
    {
        string report=args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"render-benchmark.json");
        try
        {
            var surface=new RadialSurface{Width=632,Height=632};surface.Configure(Settings.Defaults());
            surface.Measure(new Size(632,632));surface.Arrange(new Rect(0,0,632,632));surface.UpdateLayout();
            surface.Gesture.Begin(8,surface.OuterRadius,58);surface.Gesture.Update(-140,0,0);surface.Gesture.Update(-140,0,150);surface.Gesture.Update(140,0,160);surface.Gesture.Update(140,0,310);
            var bitmap=new RenderTargetBitmap(632,632,96,96,PixelFormats.Pbgra32);
            void Frame(int i)
            {
                double a=Math.Sin(i*.11)*.15;surface.Gesture.Update(259*Math.Cos(a),259*Math.Sin(a),400+i*16);surface.Selected=surface.Gesture.Hovered;
                surface.InvalidateVisual();surface.UpdateLayout();bitmap.Clear();bitmap.Render(surface);
            }
            for(int i=0;i<24;i++)Frame(i);
            const int frames=180;
            long allocated=GC.GetAllocatedBytesForCurrentThread();int gen0=GC.CollectionCount(0);var watch=Stopwatch.StartNew();
            for(int i=0;i<frames;i++)Frame(i);
            watch.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            File.WriteAllText(report,JsonSerializer.Serialize(new {Success=true,Version=typeof(Program).Assembly.GetName().Version?.ToString(),Frames=frames,ElapsedMilliseconds=watch.Elapsed.TotalMilliseconds,ManagedBytes=allocated,ManagedBytesPerFrame=allocated/(double)frames,Generation0Collections=GC.CollectionCount(0)-gen0,Size="632x632 at 96dpi",Scenario="8 items; 2 selected; live pointer moving within the right confirmation sector",Scope="WPF in-process rendering microbenchmark; not a physical input or full application performance test"},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{Success=false,Error=ex.ToString()}));return 1;}
    }
}
