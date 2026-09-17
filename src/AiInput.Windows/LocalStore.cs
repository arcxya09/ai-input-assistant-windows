using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.IO.Compression;
namespace AiInput.Windows;

public sealed class Settings
{
    public int Schema { get; set; }=1;
    public bool AutoUpdate { get; set; }=true;
    public double FontSize { get; set; }=16;
    public double Opacity { get; set; }=.85;
    public int X { get; set; }=120;
    public int Y { get; set; }=120;
    public int Width { get; set; }=420;
    public int Height { get; set; }=170;
    public string Monitor { get; set; }="";
    public uint ToggleKey { get; set; }=0x20;
    public uint ScreenshotKey { get; set; }=0x53;
    public uint AcceptKey { get; set; }=0x0D;
}
public static class LocalStore
{
    public static string Root {get;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AiInputAssistant");
    public static Settings Load()
    {
        try
        {
            var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Root,"settings.json")))??new();
            if(s.Schema!=1)return new();
            s.FontSize=Math.Clamp(s.FontSize,12,32);s.Opacity=Math.Clamp(s.Opacity,.25,1);
            s.Width=Math.Clamp(s.Width,260,1000);s.Height=Math.Clamp(s.Height,110,700);
            return s;
        }
        catch{return new();}
    }
    public static void Save(Settings s)
    {
        Directory.CreateDirectory(Root);
        string path=Path.Combine(Root,"settings.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(s,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true);
    }
    public static void SaveKey(string key)
    {
        Directory.CreateDirectory(Root);
        byte[] raw=Encoding.UTF8.GetBytes(key.Trim());
        try { File.WriteAllBytes(Path.Combine(Root,"key.bin"),ProtectedData.Protect(raw,null,DataProtectionScope.CurrentUser)); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
    public static string ReadKey()
    {
        try
        {
            byte[] raw=ProtectedData.Unprotect(File.ReadAllBytes(Path.Combine(Root,"key.bin")),null,DataProtectionScope.CurrentUser);
            try{return Encoding.UTF8.GetString(raw);}finally{CryptographicOperations.ZeroMemory(raw);}
        }
        catch{return "";}
    }
    public static void DeleteKey(){string p=Path.Combine(Root,"key.bin");if(File.Exists(p))File.Delete(p);}
    static readonly object Sync=new();
    public static void Log(string code,Exception? error=null)
    {
        lock(Sync)try
        {
            string dir=Path.Combine(Root,"logs");Directory.CreateDirectory(dir);
            string file=Path.Combine(dir,"events.log");
            if(File.Exists(file)&&new FileInfo(file).Length>2*1024*1024)
            {
                for(int i=4;i>=1;i--){string from=i==1?file:file+"."+(i-1);if(File.Exists(from))File.Move(from,file+"."+i,true);}
            }
            File.AppendAllText(file,JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,version=AiInput.Core.ProductInfo.VersionText,code,
                errorType=error?.GetType().Name,hresult=error?.HResult})+Environment.NewLine);
        }catch{}
    }
    public static void Export(string destination,Settings settings)
    {
        using var archive=ZipFile.Open(destination,ZipArchiveMode.Create);
        string dir=Path.Combine(Root,"logs");
        if(Directory.Exists(dir))foreach(string file in Directory.GetFiles(dir,"events.log*"))archive.CreateEntryFromFile(file,Path.GetFileName(file));
        using var writer=new StreamWriter(archive.CreateEntry("settings.json").Open());
        writer.Write(JsonSerializer.Serialize(settings));
    }
}
