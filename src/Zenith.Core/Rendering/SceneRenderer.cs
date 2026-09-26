using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ScriptedEngine;
using SkiaSharp;
using Zenith.Core.Midi;
using Zenith.Core.Scripted;
using ZenithEngine;

namespace Zenith.Core.Rendering;

public static class ModuleCatalog
{
    public static readonly string[] BuiltinIds=["classic","flat","pfa","miditrail","textured","notecounter"];
    public static IPluginRender Create(string id,RenderSettings settings)=>id switch
    {
        "classic"=>new ClassicRender.Render(settings),"flat"=>new FlatRender.Render(settings),"pfa"=>new PFARender.Render(settings),"miditrail"=>new MIDITrailRender.Render(settings),"textured"=>new TexturedRender.Render(settings),"notecounter"=>new NoteCountRender.Render(settings),_=>throw new ArgumentException($"Unknown module {id}")
    };
    public static IEnumerable<IPluginRender> Discover(string folder,RenderSettings settings)
    {
        foreach(string file in Directory.Exists(folder)?Directory.GetFiles(folder,"*.dll"):[]) {
            System.Reflection.Assembly assembly;
            try {assembly=System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file));}
            catch(Exception e){throw new InvalidDataException($"Cannot load plugin {file}. Plugins must target the macOS Zenith.Core API; Windows WPF DLLs require a source port.",e);}
            foreach(var type in assembly.GetTypes().Where(t=>!t.IsAbstract&&typeof(IPluginRender).IsAssignableFrom(t)))
                yield return (IPluginRender)Activator.CreateInstance(type,settings)!;
        }
    }
}

/// <summary>Owns GL resources on the calling render thread; the UI may edit the supplied plugin settings.</summary>
public sealed class SceneRenderer : IDisposable
{
    readonly NativeContext context;
    readonly RenderTarget target;
    readonly ScenePostProcessor postProcessor;
    readonly ScriptedGlRenderer quads;
    readonly MidiSequence midi;
    readonly RenderSettings settings;
    readonly IPluginRender? plugin;
    readonly ScriptedPack? script;
    readonly FastList<Note> visible=new();
    readonly FastList<Note>.Iterator collector;
    readonly NoteColor[][] colors;
    readonly NoteColor[][] baseColors;
    readonly PaletteSelection paletteSelection;
    readonly System.Reflection.FieldInfo? modulePaletteField;
    long paletteRevision;
    int cursor;
    double previous=double.NegativeInfinity;
    bool sessionStarted;
    bool pluginLifecycleStarted;
    bool disposed;
    public double ScreenTime {get;set;}=300;
    public int FirstKey {get;set;}=0;
    public int LastKey {get;set;}=128;
    public string Device=>context.Description;
    public double NoteScreenTime=>script?.NoteScreenTime??plugin?.NoteScreenTime??ScreenTime;
    public long LastNoteCount=>script?.LastNoteCount??plugin?.LastNoteCount??0;
    public SceneRenderer(MidiSequence midi,RenderSettings settings,IPluginRender? plugin=null,ScriptedPack? script=null,string? palette=null,PaletteSelection? paletteSelection=null)
    {
        this.midi=midi;this.settings=settings;this.plugin=plugin;this.script=script;
        collector=visible.Iterate();
        try
        {
            context = new();
            target = new(settings.width, settings.height);
            quads = new();
            this.paletteSelection = paletteSelection ?? (plugin != null
                ? PaletteService.For(plugin.SettingsControl, plugin is PFARender.Render ? .8f : 1f)
                : new PaletteSelection());
            modulePaletteField = plugin?.SettingsControl.GetType().GetField("palette");
            this.paletteSelection.Select(palette ?? modulePaletteField?.GetValue(plugin!.SettingsControl) as string
                ?? this.paletteSelection.SelectedImage);
            // An explicit constructor argument is authoritative. Synchronize the
            // module field as well, because its reload and future live-frame
            // checks use that field as the selected palette.
            if (palette != null)
                modulePaletteField?.SetValue(plugin!.SettingsControl, this.paletteSelection.SelectedImage);
            paletteRevision = this.paletteSelection.Revision;
            var pal = this.paletteSelection.GetColors(midi.Tracks.Length);
            colors = Enumerable.Range(0, midi.Tracks.Length).Select(t => Enumerable.Range(0, 16)
                .Select(c => new NoteColor { left = pal[t * 32 + c * 2], right = pal[t * 32 + c * 2 + 1] }).ToArray()).ToArray();
            if (plugin != null)
            {
                plugin.NoteColors = colors;
                plugin.CurrentMidi = new()
                {
                    division = midi.Division,
                    trackCount = midi.Tracks.Length,
                    noteCount = midi.NoteCount,
                    tickLength = midi.LengthTicks,
                    secondsLength = midi.DurationSeconds,
                    firstTempo = (int)(60_000_000 / midi.TempoMap.BeatsPerMinuteAt(0)),
                    timeSig = new()
                    {
                        numerator = midi.TimeSignatures.Length == 0 ? 4 : midi.TimeSignatures[0].Numerator,
                        denominator = midi.TimeSignatures.Length == 0 ? 4 : midi.TimeSignatures[0].Denominator
                    }
                };
                // Init may allocate resources and then throw. Its Dispose must
                // still run while this scene's context is current.
                pluginLifecycleStarted = true;
                plugin.Init();
                plugin.ReloadTrackColors();
            }
            // The module can apply its own selected palette in ReloadTrackColors.
            // Snapshot after that call so frame updates cannot restore stale colors.
            baseColors = colors.Select(t => t.Select(c => new NoteColor { left = c.left, right = c.right }).ToArray()).ToArray();
            postProcessor = new(settings.width, settings.height, settings.downscale, settings.BGImage);
        }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { failure.Data["SceneCleanupFailure"] = cleanup; }
            throw;
        }
    }
    double Units(double seconds)=>settings.timeBasedNotes?seconds*1000:midi.TempoMap.SecondsToTick(seconds);
    /// <summary>Apply the picker selection on the render thread, retaining note and particle state.</summary>
    public void ReloadPalette(string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        paletteSelection.Select(name);
        modulePaletteField?.SetValue(plugin!.SettingsControl, paletteSelection.SelectedImage);
        ReloadPaletteColors();
    }
    void ReloadPaletteColors()
    {
        long revision = paletteSelection.Revision;
        var palette = paletteSelection.GetColors(midi.Tracks.Length);
        for (int track = 0; track < colors.Length; track++)
        for (int channel = 0; channel < 16; channel++)
        {
            var original = baseColors[track][channel];
            original.left = palette[track * 32 + channel * 2];
            original.right = palette[track * 32 + channel * 2 + 1];
            if (colors[track][channel].isDefault)
            {
                colors[track][channel].left = original.left;
                colors[track][channel].right = original.right;
            }
        }
        paletteRevision = revision;
    }
    RenderOptions Options(double time)
    {
        var signature=new TimeSignature{numerator=midi.TimeSignatures.Length==0?4:midi.TimeSignatures[0].Numerator,denominator=midi.TimeSignatures.Length==0?4:midi.TimeSignatures[0].Denominator};
        return new(){firstKey=FirstKey,lastKey=LastKey,renderWidth=settings.width,renderHeight=settings.height,renderAspectRatio=(double)settings.width/settings.height,renderFPS=settings.fps,renderSSAA=settings.downscale,midiTime=time,noteScreenTime=ScreenTime,midiPPQ=midi.Division,midiTimeBased=settings.timeBasedNotes,midiTimeSignature=signature,midiBarLength=midi.Division*signature.numerator/signature.denominator*4};
    }
    public byte[] Render(double seconds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        context.MakeCurrent();double time=Units(seconds);
        if (modulePaletteField?.GetValue(plugin!.SettingsControl) is string selectedPalette)
            paletteSelection.Select(selectedPalette);
        if (paletteRevision != paletteSelection.Revision) ReloadPaletteColors();
        if(seconds<previous)throw new InvalidOperationException("Create a new rendering session when seeking backwards so script particle state is reset.");
        previous=seconds;
        var options=Options(time);
        if(!sessionStarted){script?.Reset(options);sessionStarted=true;}
        double screen=script!=null?Math.Max(script.NoteScreenTime,ScreenTime):plugin?.NoteScreenTime??ScreenTime;
        double offset=script?.NoteCollectorOffset??plugin?.NoteCollectorOffset??0;
        bool manual=script?.ManualNoteDelete??plugin?.ManualNoteDelete??false;
        double cutoff=time+offset;
        collector.Reset();
        while(collector.MoveNext(out var current))
        {
            if(manual?current.delete:current.hasEnded&&current.end<cutoff)collector.Remove();
            // Note starts remain sorted in MIDI cursor order. Future notes cannot
            // have ended before cutoff, so the normal collector can stop here.
            else if(!manual&&current.start>cutoff)break;
        }
        double horizon=time+Math.Max(0,screen);
        while(cursor<midi.Notes.Length)
        {
            var n=midi.Notes[cursor];
            double start=settings.timeBasedNotes?n.StartSeconds*1000:n.StartTick;
            if(start>horizon)break;
            cursor++;
            double end=settings.timeBasedNotes?n.EndSeconds*1000:n.EndTick;
            if(end<cutoff&&!manual)continue;
            visible.Add(new(){start=start,end=end,hasEnded=true,key=n.Key,channel=n.Channel,vel=n.Velocity,track=n.Track,color=colors[n.Track][n.Channel]});
        }
        for(int t=0;t<colors.Length;t++)for(int c=0;c<16;c++) {var e=settings.ignoreColorEvents?null:midi.GetEmbeddedColor(t,c,Math.Max(0,seconds));var col=colors[t][c];col.isDefault=e==null; if(e is {} v){col.left=new Color4(v.Left.R,v.Left.G,v.Left.B,v.Left.A);col.right=new Color4(v.Right.R,v.Right.G,v.Right.B,v.Right.A);}else{col.left=baseColors[t][c].left;col.right=baseColors[t][c].right;}}
        GL.BindFramebuffer(FramebufferTarget.Framebuffer,target.Framebuffer);GL.Viewport(0,0,settings.width,settings.height);GL.ClearColor(0,0,0,0);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
        if(script!=null)quads.Draw(script.Render(visible,options),options.renderAspectRatio);
        else if(plugin!=null){plugin.Tempo=midi.TempoMap.BeatsPerMinuteAt(Math.Max(0,seconds));plugin.RenderFrame(visible,time,target.Framebuffer);}
        var error=GL.GetError();if(error!=ErrorCode.NoError)throw new InvalidOperationException($"OpenGL rendering failed: {error}");
        return postProcessor.Render(target.Texture, settings.ffRender && settings.ffRenderMask);
    }
    public static void SavePng(string path,byte[] pixels,int width,int height){using var image=new SKBitmap(new SKImageInfo(width,height,SKColorType.Bgra8888,SKAlphaType.Unpremul));System.Runtime.InteropServices.Marshal.Copy(pixels,0,image.GetPixels(),pixels.Length);using var data=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(path);data.SaveTo(file);}
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        void Attempt(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }

        // Each owner gets a cleanup attempt even if an earlier plugin, script,
        // or driver operation fails. The native context is always destroyed last.
        if (context != null) Attempt(context.MakeCurrent);
        if (pluginLifecycleStarted && plugin != null) Attempt(plugin.Dispose);
        if (sessionStarted && script != null) Attempt(script.EndRendering);
        if (quads != null) Attempt(quads.Dispose);
        if (postProcessor != null) Attempt(postProcessor.Dispose);
        if (target != null) Attempt(target.Dispose);
        visible.Unlink();
        if (context != null) Attempt(context.Dispose);
        if (failures?.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures?.Count > 1)
            throw new AggregateException("Multiple resources failed while closing the render session.", failures);
    }
}
