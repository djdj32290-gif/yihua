using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher;
internal static partial class SelfTests
{
    // Rendering and gesture tests use explicit examples; shipping defaults stay empty.
    static Settings DemoSettings()
    {
        var settings=Settings.Defaults();settings.Items=new()
        {
            new(){Name="文件资源管理器",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe")},
            new(){Name="记事本",Target=Path.Combine(Environment.SystemDirectory,"notepad.exe")},
            new(){Name="计算器",Target=Path.Combine(Environment.SystemDirectory,"calc.exe")},
            new(){Name="下载",Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads")},
            new(){Name="文档",Target=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)},
            new(){Name="图片",Target=Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)},
            new(){Name="桌面",Target=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)},
            new(){Name="用户文件夹",Target=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}
        };return settings;
    }
    sealed class IconHttp(Func<Uri,HttpResponseMessage> respond):HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {Interlocked.Increment(ref Requests);return Task.FromResult(respond(request.RequestUri!));}
    }
    sealed class ForwardStream(byte[] bytes):MemoryStream(bytes){public override bool CanSeek=>false;}
    static byte[] IconPng()
    {
        byte[] pixels=Enumerable.Range(0,16).SelectMany(_=>new byte[]{32,80,220,255}).ToArray();
        var bitmap=BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,pixels,16);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    static HttpResponseMessage IconResponse(byte[]? bytes)=>bytes==null?new(HttpStatusCode.NotFound):new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    static void VerifyWebsiteIconModel(string root,Action<bool,string> check)
    {
        check(Settings.Defaults().Items.Count==0 && new ConfigStore(Path.Combine(root,"missing-settings.json")).Load().Items.Count==0,"全新配置没有预置应用或文件夹入口");
        var saved=DemoSettings();var store=new ConfigStore(Path.Combine(root,"existing-settings.json"));store.Save(saved);
        check(store.Load().Items.Select(x=>x.Target).SequenceEqual(saved.Items.Select(x=>x.Target)),"改为空白默认不会清空已有配置");
        check(WebsiteIcons.Origin("https://user:password@example.org:8443/private?q=secret#part")=="https://example.org:8443","网站图标请求只使用站点来源，不携带文档路径和凭据");
        check(WebsiteIcons.Origin("file:///C:/private.txt")==null && WebsiteIcons.Origin("javascript:alert(1)")==null,"非网站目标不发起图标网络请求");
        var discovered=WebsiteIcons.Discover(new Uri("https://example.org/"),"<LINK href='/assets/logo.png?a=1&amp;b=2' REL='shortcut ICON'><link rel=apple-touch-icon href=apple.png><link rel=icon type='image/svg+xml' href='/vector.svg'><link rel=icon href='file:///private.png'><link rel=stylesheet href=style.css>").ToArray();
        check(discovered.Select(x=>x.AbsoluteUri).SequenceEqual(new[]{"https://example.org/assets/logo.png?a=1&b=2","https://example.org/apple.png"}),"解析网站声明的相对图标、大小写及 HTML 转义并排除不可解码目标");
        byte[] png=IconPng();string folder=Path.Combine(root,"web-cache");
        using var conventionalHandler=new IconHttp(_=>IconResponse(png));using var http=new HttpClient(conventionalHandler);
        var image=WebsiteIcons.LoadAsync("https://conventional.example",folder,http).GetAwaiter().GetResult();
        check(image is {IsFrozen:true} && conventionalHandler.Requests==1,"约定 favicon 图标可以解码，冻结后可跨线程更新");
        using var offlineHandler=new IconHttp(_=>throw new HttpRequestException("offline"));using var offline=new HttpClient(offlineHandler);
        check(WebsiteIcons.LoadAsync("https://conventional.example",folder,offline).GetAwaiter().GetResult()!=null && offlineHandler.Requests==0,"断网时直接读取新鲜磁盘缓存，不重复请求");
        using var declaredHandler=new IconHttp(uri=>IconResponse(uri.AbsolutePath=="/"?Encoding.UTF8.GetBytes("<link rel=icon href='/custom/site.png'>"):uri.AbsolutePath=="/custom/site.png"?png:null));using var declared=new HttpClient(declaredHandler);
        check(WebsiteIcons.LoadAsync("https://declared.example",folder,declared).GetAwaiter().GetResult()!=null && declaredHandler.Requests==3,"favicon 不存在时读取网站声明的自定义图标");
        check(WebsiteIcons.LoadAsync("https://offline.example",folder,offline).GetAwaiter().GetResult()==null,"网站不可访问时正常回退，不抛出异常");
        string badCache=WebsiteIcons.CachePath("https://bad-cache.example",folder);File.WriteAllText(badCache,"broken");
        check(WebsiteIcons.LoadAsync("https://bad-cache.example",folder,http).GetAwaiter().GetResult()!=null && File.ReadAllBytes(badCache).SequenceEqual(png),"损坏的图标缓存可重新获取并修复");
        File.SetLastWriteTimeUtc(WebsiteIcons.CachePath("https://conventional.example",folder),DateTime.UtcNow.AddDays(-8));int before=conventionalHandler.Requests;
        check(WebsiteIcons.LoadAsync("https://conventional.example",folder,http).GetAwaiter().GetResult()!=null && conventionalHandler.Requests>before,"过期的站点图标重新获取");
        using var oversizedHandler=new IconHttp(_=>new(HttpStatusCode.OK){Content=new StreamContent(new ForwardStream(new byte[WebsiteIcons.MaxIconBytes+1]))});using var oversized=new HttpClient(oversizedHandler);
        check(WebsiteIcons.LoadAsync("https://oversized.example",folder,oversized).GetAwaiter().GetResult()==null,"无长度响应也限制下载大小，不接受超大图标");
        string trimFolder=Path.Combine(root,"trim-cache");Directory.CreateDirectory(trimFolder);
        for(int i=0;i<70;i++)File.WriteAllBytes(WebsiteIcons.CachePath("https://"+i+".example",trimFolder),png);
        WebsiteIcons.Trim(trimFolder);check(Directory.GetFiles(trimFolder,"*.icon").Length<=WebsiteIcons.MaxDiskFiles,"站点磁盘缓存最多保留 64 项");
        string largeFolder=Path.Combine(root,"large-cache");Directory.CreateDirectory(largeFolder);
        for(int i=0;i<20;i++){using var file=File.Create(WebsiteIcons.CachePath("https://"+i+".example",largeFolder));file.SetLength(WebsiteIcons.MaxIconBytes);}
        WebsiteIcons.Trim(largeFolder);check(new DirectoryInfo(largeFolder).GetFiles("*.icon").Sum(f=>f.Length)<=WebsiteIcons.MaxDiskBytes,"站点磁盘缓存总量不超过 8 MiB");
        check(WebsiteIcons.Decode(Encoding.UTF8.GetBytes("<html>not an icon</html>"))==null,"非图标响应不替换默认图标");
    }
    static async Task VerifyWebsiteIconRefresh(AppController controller,SettingsWindow window,string folder,Action<bool,string> check)
    {
        var original=controller.Config.Items;string oldCache=WebsiteIcons.CacheDirectory;var oldClient=WebsiteIcons.TestClient;bool oldEnabled=WebsiteIcons.AutomaticLoading;
        byte[] png=IconPng();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler=new DelayedIconHttp(gate.Task,png);using var http=new HttpClient(handler);
        try
        {
            WebsiteIcons.TestClient=http;WebsiteIcons.CacheDirectory=Path.Combine(folder,"web-icon-test-cache");WebsiteIcons.AutomaticLoading=true;
            controller.Config.Items=Settings.Defaults().Items;controller.Apply();window.RefreshPreview();window.UpdateLayout();
            var surface=(RadialSurface)window.FindName("WheelPreview");
            check(surface.Settings.Items.Count==0 && surface.EditorHit(new Point(surface.ActualWidth/2,surface.ActualHeight/2))==-1,"默认空白轮盘仍可通过中心加号添加入口");
            SaveVisual(window,Path.Combine(folder,"首次空白轮盘-1.3.4.png"));
            string url="https://refresh-"+Guid.NewGuid().ToString("N")+".example/";
            var editor=window.CreateEditor(-1);((TextBox)editor.FindName("NameBox")).Text="网站图标验证";((TextBox)editor.FindName("TargetBox")).Text=url;
            try{check(editor.SaveItem() && controller.Config.Items.Count==1 && controller.Config.Items[0].Target==url,"空白默认轮盘可直接保存首个网址入口");}finally{editor.Close();}
            window.UpdateLayout();var fallback=surface.IconAt(0);
            check(fallback!=null && !WebsiteIcons.WhenLoaded(url).IsCompleted,"网站下载未完成时立即显示默认图标，设置界面保持可用");
            gate.SetResult();await WebsiteIcons.WhenLoaded(url);await Task.Delay(50);window.UpdateLayout();
            check(surface.IconAt(0) is BitmapSource {IsFrozen:true} && !ReferenceEquals(surface.IconAt(0),fallback),"网站图标后台完成后自动替换已显示的轮盘图标");
            check(handler.Requests==1 && File.Exists(WebsiteIcons.CachePath(WebsiteIcons.Origin(url)!,WebsiteIcons.CacheDirectory)),"同一站点图标请求合并并写入指定项目内缓存");
            SaveVisual(window,Path.Combine(folder,"网站图标更新-1.3.4.png"));
        }
        finally{gate.TrySetResult();WebsiteIcons.AutomaticLoading=oldEnabled;WebsiteIcons.TestClient=oldClient;WebsiteIcons.CacheDirectory=oldCache;controller.Config.Items=original;controller.Apply();window.RefreshPreview();}
    }
    sealed class DelayedIconHttp(Task ready,byte[] png):HttpMessageHandler
    {
        internal int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation){Interlocked.Increment(ref Requests);await ready.WaitAsync(cancellation);return IconResponse(png);}
    }
    public static int VerifyWebsiteIconNetwork(string[] args)
    {
        string report=args[1];try
        {
            using var http=WebsiteIcons.CreateClient();const string origin="https://github.com";
            var image=WebsiteIcons.LoadAsync(origin,WebsiteIcons.CacheDirectory,http).GetAwaiter().GetResult();if(image==null)throw new Exception("GitHub favicon 没有返回可解码图标");
            string screenshot=Path.Combine(Path.GetDirectoryName(report)!,"网站图标-1.3.4.png");var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var stream=File.Create(screenshot))png.Save(stream);
            File.WriteAllText(report,JsonSerializer.Serialize(new{Success=true,Origin=origin,Width=image.PixelWidth,Height=image.PixelHeight,CacheFile=WebsiteIcons.CachePath(origin,WebsiteIcons.CacheDirectory),Screenshot=screenshot},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{Success=false,Error=ex.ToString()}));return 1;}
    }
}
