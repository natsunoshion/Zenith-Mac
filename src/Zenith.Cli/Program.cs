using Zenith.Core.Midi;
using Zenith.Core.Export;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
using ZenithEngine;

if(args.Length==0 || args[0] is "--help" or "help"){PrintHelp();return;}
try
{
    if(args[0]=="render"){await RenderVideo(args);return;}
    if(args[0]=="inspect"){var m=MidiSequence.Load(args[1]);Console.WriteLine($"{m.NoteCount:N0} notes | {m.Tracks.Length} tracks | {m.Division} PPQ | {m.DurationSeconds:0.000}s");return;}
    var midi=MidiSequence.Load(args[1]);
    var modules=args[0]=="gpu-test"?ModuleCatalog.BuiltinIds.Concat(new[]{AssetPaths.Resolve("Plugins/Assets/Scripted/Resources/Synthesia X.zrp")}).ToArray():new[]{args.Length>3?args[3]:"classic"};
    int failures=0;
    foreach(var module in modules)
    {
        try {
            var settings=new RenderSettings{width=960,height=540};IPluginRender? plugin=null;ScriptedPack? pack=null;
            if(ModuleCatalog.BuiltinIds.Contains(module))plugin=ModuleCatalog.Create(module,settings);else pack=ScriptedPack.Load(module);
            if(plugin is TexturedRender.Render textured){var ts=(TexturedRender.Settings)textured.SettingsControl;ts.currPack=new TexturedRender.PackLoader().LoadPack(AssetPaths.Resolve("Plugins/Assets/Textured/Resources/Default"),TexturedRender.PackType.Folder);if(ts.currPack.error)throw new Exception(ts.currPack.description);ts.lastPackChangeTime=1;}
            using(pack)using(var renderer=new SceneRenderer(midi,settings,plugin,pack)){Console.WriteLine(renderer.Device);byte[] pixels=[];double until=args.Length>4?double.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture):2.0;for(int i=0;i<=until*60;i++)pixels=renderer.Render(i/60d);string path=args[0]=="gpu-test"?Path.Combine(args[2],Path.GetFileNameWithoutExtension(module)+".png"):args[2];Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);SceneRenderer.SavePng(path,pixels,settings.width,settings.height);if(!pixels.Where((_,i)=>i%4!=3).Any(b=>b!=0))throw new Exception("Rendered black frame");Console.WriteLine($"PASS {module}: {path}");}
        }catch(Exception e){Console.Error.WriteLine($"FAIL {module}: {e}");failures++;}
    }
    Environment.ExitCode=failures==0?0:1;
}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}

static void PrintHelp() => Console.WriteLine("""
Zenith macOS CLI
  inspect <midi>
  frame <midi> <output.png> [module|pack.zrp] [seconds]
  gpu-test <midi> <output-folder>
  render <midi> <output.mp4> [module|pack.zrp] [options]

Render options:
  --width 1920 --height 1080 --fps 60 --ssaa 1
  --start 0 --duration <seconds> --speed 1 --screen-time 300
  --audio <audio.wav> --audio-offset <seconds> --audio-trim <seconds>
  --mask <mask.mp4> --crf 17 --preset medium --bitrate <kbps>
  --codec libx264|h264_videotoolbox  (hardware requires --bitrate)
  --ffmpeg <executable> --custom '<ffmpeg output options>' --time-based
The default duration runs from --start to the MIDI end at --speed.
""");

static async Task RenderVideo(string[] arguments)
{
    if(arguments.Length<3)throw new ArgumentException("render requires MIDI input and video output paths.");
    int firstOption=3;
    string module="classic";
    if(arguments.Length>3&&!arguments[3].StartsWith("--")){module=arguments[3];firstOption=4;}
    var options=new Dictionary<string,string>(StringComparer.Ordinal);
    for(int i=firstOption;i<arguments.Length;i++)
    {
        string key=arguments[i];
        if(key=="--time-based"){options.Add(key,"true");continue;}
        if(!key.StartsWith("--")||i+1>=arguments.Length)throw new ArgumentException($"Missing value for option {key}.");
        options.Add(key,arguments[++i]);
    }
    string[] valid=["--width","--height","--fps","--ssaa","--start","--duration","--speed","--screen-time","--audio","--audio-offset","--audio-trim","--mask","--crf","--preset","--bitrate","--codec","--ffmpeg","--custom","--time-based"];
    foreach(string key in options.Keys)if(!valid.Contains(key))throw new ArgumentException($"Unknown render option {key}.");
    string? Get(string key)=>options.GetValueOrDefault(key);
    double Number(string key,double fallback)=>Get(key)is{} value?double.Parse(value,System.Globalization.CultureInfo.InvariantCulture):fallback;
    int Integer(string key,int fallback)=>Get(key)is{} value?int.Parse(value,System.Globalization.CultureInfo.InvariantCulture):fallback;
    var midi=MidiSequence.Load(arguments[1]);
    string codec=Get("--codec")??"libx264";
    if(codec is not ("libx264" or "h264_videotoolbox"))throw new ArgumentException("--codec must be libx264 or h264_videotoolbox.");
    if(codec=="h264_videotoolbox"&&Get("--bitrate")==null)throw new ArgumentException("--codec h264_videotoolbox requires an explicit --bitrate in kbps; it does not use CRF.");
    int width=Integer("--width",1920),height=Integer("--height",1080),ssaa=Integer("--ssaa",1);
    double fps=Number("--fps",60),start=Number("--start",0),speed=Number("--speed",1);
    if(ssaa is <1 or >8)throw new ArgumentException("--ssaa must be from 1 to 8.");
    if(fps%1!=0)throw new ArgumentException("The original renderer API requires an integer frame rate.");
    var settings=new RenderSettings{width=checked(width*ssaa),height=checked(height*ssaa),downscale=ssaa,fps=checked((int)fps),timeBasedNotes=options.ContainsKey("--time-based"),ffRender=true,ffRenderMask=!string.IsNullOrWhiteSpace(Get("--mask"))};
    IPluginRender? plugin=null;ScriptedPack? pack=null;
    if(ModuleCatalog.BuiltinIds.Contains(module))plugin=ModuleCatalog.Create(module,settings);else pack=ScriptedPack.Load(module);
    if(plugin is TexturedRender.Render textured)
    {
        var ts=(TexturedRender.Settings)textured.SettingsControl;
        ts.currPack=new TexturedRender.PackLoader().LoadPack(AssetPaths.Resolve("Plugins/Assets/Textured/Resources/Default"),TexturedRender.PackType.Folder);
        if(ts.currPack.error)throw new InvalidDataException(ts.currPack.description);
        ts.lastPackChangeTime=1;
    }
    using(pack)
    using(var renderer=new SceneRenderer(midi,settings,plugin,pack))
    using(var cancellation=new CancellationTokenSource())
    {
        renderer.ScreenTime=Number("--screen-time",settings.timeBasedNotes?2000:300);
        ConsoleCancelEventHandler cancel=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
        Console.CancelKeyPress+=cancel;
        try
        {
            Console.WriteLine($"{renderer.Device}\nRendering {module}: {width}x{height} @ {fps} fps");
            var exportOptions=new VideoExportOptions
            {
                OutputPath=arguments[2],MaskOutputPath=Get("--mask"),Width=width,Height=height,FramesPerSecond=fps,
                DurationSeconds=Number("--duration",(midi.DurationSeconds-start)/speed),StartSeconds=start,PlaybackSpeed=speed,
                AudioPath=Get("--audio"),AudioOffsetSeconds=Number("--audio-offset",Math.Max(0,-start/speed)),AudioTrimSeconds=Number("--audio-trim",Math.Max(0,start)),
                Crf=Integer("--crf",17),Preset=Get("--preset")??"medium",BitrateKbps=Get("--bitrate")is null?null:Integer("--bitrate",20000),VideoCodec=codec,
                FfmpegPath=Get("--ffmpeg")??"ffmpeg",AdditionalArguments=FfmpegExporter.ParseArguments(Get("--custom")??"")
            };
            var result=await FfmpegExporter.ExportAsync(exportOptions,(frame,token)=>
            {
                token.ThrowIfCancellationRequested();
                var pixels=renderer.Render(frame.TimelineSeconds);
                if(frame.Index%(long)Math.Max(1,fps)==0)Console.WriteLine($"Frame {frame.Index}/{frame.TotalFrames} ({100.0*frame.Index/frame.TotalFrames:0.0}%)");
                return ValueTask.FromResult<ReadOnlyMemory<byte>>(pixels);
            },cancellationToken:cancellation.Token);
            Console.WriteLine($"PASS: {result.OutputPath} | {result.FrameCount} frames | {result.Elapsed.TotalSeconds:0.00}s");
            if(result.MaskOutputPath!=null)Console.WriteLine($"Mask: {result.MaskOutputPath}");
        }
        finally{Console.CancelKeyPress-=cancel;}
    }
}
