using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OrbitLauncher;
internal static class WebsiteIcons
{
    internal const int MaxIconBytes=512*1024,MaxPageBytes=256*1024,MaxDiskFiles=64,MaxDiskBytes=8*1024*1024;
    internal static bool AutomaticLoading=true;
    internal static HttpClient? TestClient;
    internal static string CacheDirectory=Path.Combine(AppContext.BaseDirectory,"Data","WebsiteIcons");
    internal static event Action<string>? Updated;
    sealed class Entry { internal ImageSource? Image;internal Task Loading=Task.CompletedTask;internal DateTime Started=DateTime.UtcNow; }
    static readonly object gate=new(),diskGate=new();
    static readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    static readonly SemaphoreSlim downloads=new(3);
    static readonly HttpClient client=CreateClient();
    internal static HttpClient CreateClient()
    {
        var http=new HttpClient(new HttpClientHandler{UseCookies=false,AutomaticDecompression=DecompressionMethods.All,MaxAutomaticRedirections=4}){Timeout=TimeSpan.FromSeconds(8)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Yihua/1.3.8");return http;
    }
    internal static string? Origin(string target)
    {
        if(!Uri.TryCreate(Launcher.Expand(target),UriKind.Absolute,out var uri) || uri.Scheme is not ("http" or "https"))return null;
        // Fetch public icon resources without URL credentials, document paths or query strings.
        return new UriBuilder(uri.Scheme,uri.Host,uri.IsDefaultPort?-1:uri.Port).Uri.GetLeftPart(UriPartial.Authority);
    }
    internal static ImageSource? For(string target)
    {
        string? origin=Origin(target);if(origin==null)return null;
        lock(gate)
        {
            if(entries.TryGetValue(origin,out var known))
            {
                if(known.Image!=null || DateTime.UtcNow-known.Started<TimeSpan.FromMinutes(5))return known.Image;
                entries.Remove(origin);
            }
            if(!AutomaticLoading)return null;
            if(entries.Count>=128)entries.Remove(entries.OrderBy(p=>p.Value.Started).First().Key);
            var entry=new Entry();entries[origin]=entry;var http=TestClient??client;string folder=CacheDirectory;
            entry.Loading=Task.Run(async ()=>
            {
                await downloads.WaitAsync().ConfigureAwait(false);
                try
                {
                    var image=await LoadAsync(origin,folder,http).ConfigureAwait(false);
                    if(image==null)return;
                    lock(gate)entry.Image=image;
                    var dispatcher=Application.Current?.Dispatcher;
                    if(dispatcher!=null && !dispatcher.HasShutdownStarted)_=dispatcher.BeginInvoke(()=>Updated?.Invoke(origin));
                }
                catch { /* Offline, denied cache writes and unreadable icons keep the fallback. */ }
                finally{downloads.Release();}
            });
            return null;
        }
    }
    internal static Task WhenLoaded(string target)
    {lock(gate)return Origin(target) is {} origin && entries.TryGetValue(origin,out var entry)?entry.Loading:Task.CompletedTask;}
    internal static string CachePath(string origin,string folder)=>Path.Combine(folder,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(origin))).ToLowerInvariant()+".icon");
    internal static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream=new MemoryStream(bytes,false);
            var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
            var frame=decoder.Frames.FirstOrDefault();if(frame==null || frame.PixelWidth<1 || frame.PixelHeight<1 || frame.PixelWidth>4096 || frame.PixelHeight>4096)return null;
            stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=48;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
        }
        catch{return null;}
    }
    internal static async Task<BitmapSource?> LoadAsync(string origin,string folder,HttpClient http)
    {
        string path=CachePath(origin,folder);
        try
        {
            var cached=new FileInfo(path);
            if(cached.Exists && cached.Length<=MaxIconBytes && DateTime.UtcNow-cached.LastWriteTimeUtc<TimeSpan.FromDays(7))
            {var image=Decode(await File.ReadAllBytesAsync(path).ConfigureAwait(false));if(image!=null)return image;}
        }
        catch { }
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var home=new Uri(origin+"/");var candidates=new List<Uri>{new(home,"favicon.ico")};
        // Most sites expose this small conventional resource; avoid downloading the page when it works.
        var conventional=await TryIcon(candidates[0]).ConfigureAwait(false);if(conventional!=null)return conventional;
        var html=await ReadAsync(home,MaxPageBytes,http,timeout.Token).ConfigureAwait(false);
        if(html!=null)foreach(var icon in Discover(home,Encoding.UTF8.GetString(html)).Take(4))
        {if(candidates.Contains(icon))continue;candidates.Add(icon);var image=await TryIcon(icon).ConfigureAwait(false);if(image!=null)return image;}
        return null;

        async Task<BitmapSource?> TryIcon(Uri uri)
        {
            var bytes=await ReadAsync(uri,MaxIconBytes,http,timeout.Token).ConfigureAwait(false);if(bytes==null)return null;
            var image=Decode(bytes);if(image==null)return null;
            try
            {
                // Cache only under the portable folder; there is no AppData or OS web-cache fallback.
                lock(diskGate){Directory.CreateDirectory(folder);File.WriteAllBytes(path,bytes);Trim(folder);}
            }
            catch { }
            return image;
        }
    }
    static async Task<byte[]?> ReadAsync(Uri uri,int maximum,HttpClient http,CancellationToken cancellation)
    {
        try
        {
            if(uri.Scheme is not ("http" or "https") || uri.UserInfo.Length!=0)return null;
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
            if(!response.IsSuccessStatusCode || response.Content.Headers.ContentLength>maximum)return null;
            using var stream=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);using var buffer=new MemoryStream();var chunk=new byte[8192];
            while(true){int read=await stream.ReadAsync(chunk,cancellation).ConfigureAwait(false);if(read==0)break;if(buffer.Length+read>maximum)return null;buffer.Write(chunk,0,read);}
            return buffer.ToArray();
        }
        catch{return null;}
    }
    internal static IEnumerable<Uri> Discover(Uri home,string html)
    {
        var urls=new List<Uri>();
        try
        {
            foreach(Match tag in Regex.Matches(html,@"<link\b[^>]{0,4096}>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)))
            {
                var attributes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(Match attr in Regex.Matches(tag.Value,"([a-zA-Z][a-zA-Z0-9_-]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",RegexOptions.None,TimeSpan.FromMilliseconds(100)))
                    attributes[attr.Groups[1].Value]=WebUtility.HtmlDecode(attr.Groups[2].Success?attr.Groups[2].Value:attr.Groups[3].Success?attr.Groups[3].Value:attr.Groups[4].Value);
                if(!attributes.TryGetValue("rel",out var rel) || !rel.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Any(p=>p.Equals("icon",StringComparison.OrdinalIgnoreCase) || p.Equals("apple-touch-icon",StringComparison.OrdinalIgnoreCase)))continue;
                if(attributes.GetValueOrDefault("type","").Contains("svg",StringComparison.OrdinalIgnoreCase))continue;
                if(attributes.TryGetValue("href",out var href) && Uri.TryCreate(home,href,out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length==0 && !uri.AbsolutePath.EndsWith(".svg",StringComparison.OrdinalIgnoreCase))urls.Add(uri);
            }
        }
        catch(RegexMatchTimeoutException) { }
        return urls.Distinct();
    }
    internal static void Trim(string folder)
    {
        var files=new DirectoryInfo(folder).GetFiles("*.icon").Where(f=>Regex.IsMatch(f.Name,@"^[a-f0-9]{64}\.icon$")).OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();
        long bytes=0;for(int i=0;i<files.Length;i++){bytes+=files[i].Length;if(i>=MaxDiskFiles || bytes>MaxDiskBytes)try{files[i].Delete();}catch{}}
    }
}
