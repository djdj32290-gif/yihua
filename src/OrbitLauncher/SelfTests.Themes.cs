using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    static double Luminance(Color color)
    {double Channel(byte v){double n=v/255d;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}return .2126*Channel(color.R)+.7152*Channel(color.G)+.0722*Channel(color.B);}
    static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);}
    static void VerifyThemeModel(Action<bool,string> check)
    {
        check(Themes.Options.Count==4 && Themes.Options.Select(p=>p.Id).Distinct().Count()==4,"四种独立配色标识");
        var old=JsonSerializer.Deserialize<Settings>("{\"Items\":[]}")!;old.Normalize();check(old.Theme=="Obsidian","旧配置没有皮肤字段时保留默认深色");
        old.Theme="invalid";old.Normalize();check(old.Theme=="Obsidian","未知皮肤安全回退为曜石黑");
        foreach(var palette in Themes.Options)
        {
            var config=Settings.Defaults();config.Theme=palette.Id;
            check(config.Copy().Theme==palette.Id && JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(config))!.Theme==palette.Id,$"{palette.Id} 皮肤复制及序列化往返");
            check(Contrast(palette.Brush("Text").Color,palette.Brush("Window").Color)>=4.5 && Contrast(palette.Brush("Muted").Color,palette.Brush("Card").Color)>=4.5,$"{palette.Id} 正文及次要文字有足够对比度");
            check(Contrast(palette.Brush("OnAccent").Color,palette.Brush("Accent").Color)>=4.5,$"{palette.Id} 主按钮文字对比度");
            check(Contrast(palette.Brush("WheelText").Color,palette.Brush("WheelCenter").Color)>=4.5,$"{palette.Id} 轮盘中心文字对比度");
            check(ContinuousRing(8,false,palette.Id),$"{palette.Id} 主题手势圆环无缝");
        }
    }

    static async Task VerifyThemes(AppController controller,SettingsWindow window,RadialSurface preview,string folder,Action<bool,string> check)
    {
        string original=controller.Config.Theme,items=JsonSerializer.Serialize(controller.Config.Items);
        var tabs=(TabControl)window.FindName("SettingsTabs");int originalTab=tabs.SelectedIndex;
        var picker=(ComboBox)window.FindName("ThemeBox");
        var editor=window.CreateEditor(controller.Config.Items.Count>0?0:-1);editor.Show();
        try
        {
            foreach(var palette in Themes.Options)
            {
                tabs.SelectedIndex=1;picker.SelectedValue=palette.Id;await Task.Delay(100);window.UpdateLayout();editor.UpdateLayout();
                check(controller.Config.Theme==palette.Id && new ConfigStore().Load().Theme==palette.Id,$"{palette.Id} 选择皮肤立即保存回读");
                check(((SolidColorBrush)window.Background).Color==palette.Brush("Window").Color && ((SolidColorBrush)editor.Background).Color==palette.Brush("Window").Color,$"{palette.Id} 已打开设置和详情同步换肤");
                check(preview.Settings.Theme==palette.Id && Themes.Current.Id==palette.Id,$"{palette.Id} 预览和全局资源同步");
                picker.IsDropDownOpen=true;await Task.Delay(40);
                var popup=(Popup)picker.Template.FindName("PART_Popup",picker);
                check(((SolidColorBrush)((Border)popup.Child).Background).Color==palette.Brush("Popup").Color,$"{palette.Id} 下拉弹层跟随主题");picker.IsDropDownOpen=false;
                SaveVisual(window,Path.Combine(folder,$"配色-{palette.Id}-外观.png"));
                SaveVisual(editor,Path.Combine(folder,$"配色-{palette.Id}-详情.png"));
                tabs.SelectedIndex=0;window.UpdateLayout();
                SaveVisual(window,Path.Combine(folder,$"配色-{palette.Id}-布局.png"));
                var selectedTab=(TabItem)tabs.Items[0];selectedTab.Focus();await Task.Delay(30);
                check(RoundedTabCorners(selectedTab),$"{palette.Id} 主题标签仍为四角圆角");
                var stored=new SettingsWindow(controller);
                check((string)((ComboBox)stored.FindName("ThemeBox")).SelectedValue==palette.Id,$"{palette.Id} 重新创建设置窗口记住皮肤");stored.Close();

                var config=Settings.Defaults();config.Theme=palette.Id;
                var wheel=new RadialSurface{Width=632,Height=632};wheel.Configure(config);
                wheel.Measure(new Size(632,632));wheel.Arrange(new Rect(0,0,632,632));wheel.UpdateLayout();
                wheel.Gesture.Begin(8,wheel.OuterRadius,58);wheel.Gesture.Update(-140,0,0);wheel.Gesture.Update(-140,0,150);wheel.Gesture.Update(140,0,160);wheel.Gesture.Update(140,0,310);wheel.Gesture.Update(259,0,320);wheel.Selected=2;wheel.InvalidateVisual();wheel.UpdateLayout();
                var image=new RenderTargetBitmap(1264,1264,192,192,PixelFormats.Pbgra32);image.Render(wheel);
                var pixels=new byte[1264*1264*4];image.CopyPixels(pixels,1264*4,0);int offset=((316+48)*2*1264+316*2)*4;
                var center=palette.Brush("WheelCenter").Color;
                check(pixels[offset]==center.B && pixels[offset+1]==center.G && pixels[offset+2]==center.R,$"{palette.Id} 实际手势轮盘采用主题中心颜色");
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(Path.Combine(folder,$"配色-{palette.Id}-手势.png")))png.Save(file);
                check(JsonSerializer.Serialize(controller.Config.Items)==items,$"{palette.Id} 换肤不改变入口配置");
            }
        }
        finally
        {
            editor.Close();controller.Config.Theme=original;picker.SelectedValue=original;controller.Apply();window.RefreshPreview();tabs.SelectedIndex=originalTab;window.UpdateLayout();
        }
    }
}
