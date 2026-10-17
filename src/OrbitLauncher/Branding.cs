using System.Windows;
using System.Windows.Media;

namespace OrbitLauncher;
public static class Branding
{
    public static DrawingImage Logo {get;}=CreateLogo();
    static DrawingImage CreateLogo()
    {
        var drawing=new DrawingGroup();
        using(var dc=drawing.Open())
        {
            var tile=new LinearGradientBrush(Color.FromRgb(25,52,76),Color.FromRgb(9,22,37),new Point(0,0),new Point(1,1));
            dc.DrawRoundedRectangle(tile,new Pen(new SolidColorBrush(Color.FromArgb(36,167,225,244)),1),new Rect(.5,.5,63,63),15,15);
            var sweep=new LinearGradientBrush(Color.FromRgb(59,221,187),Color.FromRgb(100,177,247),new Point(0,0),new Point(1,1));
            var arcPen=new Pen(sweep,5.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            dc.DrawGeometry(null,arcPen,Geometry.Parse("M 19,43 C 10,33 16,16 30,15 C 43,14 51,28 44,39 C 39,46 29,48 24,42"));
            var arrowPen=new Pen(new SolidColorBrush(Color.FromRgb(231,252,255)),5.5){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            dc.DrawGeometry(null,arrowPen,Geometry.Parse("M 24,42 L 43,23 M 32,23 L 43,23 L 43,34"));
        }
        drawing.Freeze();var logo=new DrawingImage(drawing);logo.Freeze();return logo;
    }
}
