using OpenTK.Mathematics;
using SkiaSharp;
namespace Zenith.Core.Rendering
{
    public sealed class RasterImage : IDisposable
    {
        readonly SKBitmap bitmap;
        public int Width=>bitmap.Width; public int Height=>bitmap.Height;
        public RasterImage(string path):this(File.OpenRead(path)){}
        public RasterImage(Stream stream) { using(stream) { using var codec=SKCodec.Create(stream) ?? throw new InvalidDataException("Invalid image"); bitmap=new SKBitmap(codec.Info.Width,codec.Info.Height,SKColorType.Bgra8888,SKAlphaType.Unpremul);if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success){bitmap.Dispose();throw new InvalidDataException("Invalid image pixels");} } }
        public sealed record PixelData(int Width,int Height,IntPtr Scan0);
        public PixelData LockBits()=>new(Width,Height,bitmap.GetPixels());
        public void UnlockBits(PixelData _){}
        public void Dispose()=>bitmap.Dispose();
    }
    public static class AssetPaths
    {
        public static string Root {get;set;} = FindRoot();
        static string FindRoot() { foreach(var r in new[]{AppContext.BaseDirectory,Environment.CurrentDirectory}) { var d=new DirectoryInfo(r); while(d!=null){foreach(var c in new[]{Path.Combine(d.FullName,"assets/windows/Zenith"),Path.Combine(d.FullName,"Assets/Zenith"),Path.Combine(d.FullName,"../Resources/Zenith")}) if(Directory.Exists(c))return Path.GetFullPath(c); d=d.Parent;} }return AppContext.BaseDirectory; }
        public static string Resolve(string path)=>Path.Combine(Root,path.Replace('\\','/'));
    }

}
namespace ZenithEngine
{
    public interface IPluginRender : IDisposable
    {
        string Name{get;} string Description{get;} bool Initialized{get;} object PreviewImage{get;} bool ManualNoteDelete{get;} double NoteCollectorOffset{get;}
        NoteColor[][] NoteColors{set;} double Tempo{set;} MidiInfo CurrentMidi{set;} string LanguageDictName{get;} double NoteScreenTime{get;} long LastNoteCount{get;} object SettingsControl{get;}
        void Init();void RenderFrame(FastList<Note> notes,double midiTime,int finalCompositeBuff);void ReloadTrackColors();
    }
}
