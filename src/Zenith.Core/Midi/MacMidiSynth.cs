using System.Runtime.InteropServices;

namespace Zenith.Core.Midi;

/// <summary>Apple's DLS synthesizer, hosted by an AudioUnit graph. No Windows MIDI DLLs or external synth is required.</summary>
public sealed class MacMidiSynth : IMidiOutput
{
    const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    readonly object sync = new();
    nint graph, synth;
    bool disposed;
    readonly List<byte> pendingSysEx = [];

    [StructLayout(LayoutKind.Sequential)]
    struct ComponentDescription
    {
        public uint Type, SubType, Manufacturer, Flags, FlagsMask;
    }
    static uint FourCc(string text) => (uint)text[0] << 24 | (uint)text[1] << 16 | (uint)text[2] << 8 | text[3];
    static void Check(int status, string action)
    {
        if (status != 0) throw new InvalidOperationException($"Core Audio {action} failed (OSStatus {status}).");
    }

    public static MacMidiSynth? TryCreate(out string? error)
    {
        try { error = null; return new MacMidiSynth(); }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    public MacMidiSynth()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Apple DLS synth is only available on macOS.");
        try
        {
            Check(NewAUGraph(out graph), "create graph");
            var instrument = new ComponentDescription { Type = FourCc("aumu"), SubType = FourCc("dls "), Manufacturer = FourCc("appl") };
            var device = new ComponentDescription { Type = FourCc("auou"), SubType = FourCc("def "), Manufacturer = FourCc("appl") };
            Check(AUGraphAddNode(graph, ref instrument, out int synthNode), "add synthesizer");
            Check(AUGraphAddNode(graph, ref device, out int outputNode), "add output");
            Check(AUGraphOpen(graph), "open graph");
            Check(AUGraphNodeInfo(graph, synthNode, nint.Zero, out synth), "get synthesizer");
            Check(AUGraphConnectNodeInput(graph, synthNode, 0, outputNode, 0), "connect output");
            Check(AUGraphInitialize(graph), "initialize graph");
            Check(AUGraphStart(graph), "start graph");
        }
        catch { Dispose(); throw; }
    }

    public void Send(MidiEvent message)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (message.Status < 0xF0)
                Check(MusicDeviceMIDIEvent(synth, message.Status, message.Data1, message.Data2, 0), "send MIDI");
            else if (message.Payload is { } payload)
            {
                if (message.Status == 0xF0) { pendingSysEx.Clear(); pendingSysEx.Add(0xF0); }
                pendingSysEx.AddRange(payload);
                if (pendingSysEx.Count > 0 && pendingSysEx[^1] == 0xF7)
                {
                    var packet = pendingSysEx.ToArray();
                    // A synth may reject manufacturer-specific SysEx; channel playback should continue.
                    MusicDeviceSysEx(synth, packet, (uint)packet.Length);
                    pendingSysEx.Clear();
                }
            }
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            if (disposed || synth == nint.Zero) return;
            pendingSysEx.Clear();
            for (uint channel = 0; channel < 16; channel++)
            {
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 120, 0, 0);
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 121, 0, 0);
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 0, 0, 0);
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 32, 0, 0);
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 7, 100, 0);
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 10, 64, 0);
                MusicDeviceMIDIEvent(synth, 0xB0 | channel, 11, 127, 0);
                MusicDeviceMIDIEvent(synth, 0xC0 | channel, 0, 0, 0);
                MusicDeviceMIDIEvent(synth, 0xE0 | channel, 0, 64, 0);
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (graph != nint.Zero)
            {
                AUGraphStop(graph); AUGraphUninitialize(graph); AUGraphClose(graph); DisposeAUGraph(graph);
                graph = synth = nint.Zero;
            }
        }
    }

    [DllImport(AudioToolbox)] static extern int NewAUGraph(out nint graph);
    [DllImport(AudioToolbox)] static extern int AUGraphAddNode(nint graph, ref ComponentDescription description, out int node);
    [DllImport(AudioToolbox)] static extern int AUGraphOpen(nint graph);
    [DllImport(AudioToolbox)] static extern int AUGraphNodeInfo(nint graph, int node, nint description, out nint audioUnit);
    [DllImport(AudioToolbox)] static extern int AUGraphConnectNodeInput(nint graph, int sourceNode, uint sourceOutput, int destinationNode, uint destinationInput);
    [DllImport(AudioToolbox)] static extern int AUGraphInitialize(nint graph);
    [DllImport(AudioToolbox)] static extern int AUGraphStart(nint graph);
    [DllImport(AudioToolbox)] static extern int AUGraphStop(nint graph);
    [DllImport(AudioToolbox)] static extern int AUGraphUninitialize(nint graph);
    [DllImport(AudioToolbox)] static extern int AUGraphClose(nint graph);
    [DllImport(AudioToolbox)] static extern int DisposeAUGraph(nint graph);
    [DllImport(AudioToolbox)] static extern int MusicDeviceMIDIEvent(nint unit, uint status, uint data1, uint data2, uint offsetSampleFrame);
    [DllImport(AudioToolbox)] static extern int MusicDeviceSysEx(nint unit, byte[] data, uint length);
}
