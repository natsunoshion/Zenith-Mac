using System.Runtime.InteropServices;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace Zenith.Core.Rendering;

/// <summary>Offscreen native macOS OpenGL 4.1 context. No window system or Windows runtime.</summary>
public sealed class NativeContext : IDisposable, IBindingsContext
{
    const string Framework = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    IntPtr library;
    IntPtr context;
    int vao;
    bool disposed;
    [DllImport(Framework)] static extern int CGLChoosePixelFormat(int[] attributes, out IntPtr format, out int count);
    [DllImport(Framework)] static extern int CGLCreateContext(IntPtr format, IntPtr share, out IntPtr context);
    [DllImport(Framework)] static extern int CGLDestroyPixelFormat(IntPtr format);
    [DllImport(Framework)] static extern int CGLSetCurrentContext(IntPtr context);
    [DllImport(Framework)] static extern int CGLDestroyContext(IntPtr context);
    public NativeContext()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("NativeContext requires macOS.");
        library = NativeLibrary.Load(Framework);
        try
        {
            IntPtr format = IntPtr.Zero;
            try
            {
                Check(CGLChoosePixelFormat([99, 0x4100, 73, 8, 32, 11, 8, 12, 24, 0], out format, out _));
                Check(CGLCreateContext(format, IntPtr.Zero, out context));
            }
            finally { if (format != IntPtr.Zero) CGLDestroyPixelFormat(format); }
            MakeCurrent();
            GL.LoadBindings(this);
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);
        }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { failure.Data["NativeContextCleanupFailure"] = cleanup; }
            throw;
        }
    }
    static void Check(int status) { if (status != 0) throw new InvalidOperationException($"CGL error {status}"); }
    public IntPtr GetProcAddress(string name) => NativeLibrary.TryGetExport(library, name, out var p) ? p : IntPtr.Zero;
    public void MakeCurrent() { ObjectDisposedException.ThrowIf(disposed, this); Check(CGLSetCurrentContext(context)); }
    public string Description => GL.GetString(StringName.Renderer) + " / " + GL.GetString(StringName.Version);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { (failures ??= new()).Add(error); }
        }
        var ownedContext = context;
        context = IntPtr.Zero;
        if (ownedContext != IntPtr.Zero)
        {
            Attempt(() =>
            {
                Check(CGLSetCurrentContext(ownedContext));
                if (vao != 0) GL.DeleteVertexArray(vao);
            });
            Attempt(() => Check(CGLSetCurrentContext(IntPtr.Zero)));
            Attempt(() => Check(CGLDestroyContext(ownedContext)));
        }
        vao = 0;
        var ownedLibrary = library;
        library = IntPtr.Zero;
        if (ownedLibrary != IntPtr.Zero) Attempt(() => NativeLibrary.Free(ownedLibrary));
        if (failures?.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures?.Count > 1) throw new AggregateException("Failed to release the native OpenGL context.", failures);
    }
}

public sealed class RenderTarget : IDisposable
{
    public int Framebuffer { get; }
    public int Texture { get; }
    readonly int depth;
    bool disposed;
    public int Width { get; }
    public int Height { get; }
    public RenderTarget(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width=width; Height=height;
        try
        {
            Texture=GL.GenTexture(); GL.BindTexture(TextureTarget.Texture2D,Texture);
            GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba8,width,height,0,PixelFormat.Bgra,PixelType.UnsignedByte,IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)TextureMagFilter.Linear);
            Framebuffer=GL.GenFramebuffer(); GL.BindFramebuffer(FramebufferTarget.Framebuffer,Framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,TextureTarget.Texture2D,Texture,0);
            depth=GL.GenRenderbuffer(); GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer,depth);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,RenderbufferStorage.DepthComponent24,width,height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,FramebufferAttachment.DepthAttachment,RenderbufferTarget.Renderbuffer,depth);
            if(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)!=FramebufferErrorCode.FramebufferComplete) throw new InvalidOperationException("Incomplete framebuffer");
        }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { failure.Data["RenderTargetCleanupFailure"] = cleanup; }
            throw;
        }
    }
    public byte[] ReadBgra()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var bytes=new byte[checked(Width*Height*4)]; GL.BindFramebuffer(FramebufferTarget.Framebuffer,Framebuffer);
        GL.ReadPixels(0,0,Width,Height,PixelFormat.Bgra,PixelType.UnsignedByte,bytes);
        var row=new byte[Width*4];
        for(int y=0;y<Height/2;y++){int a=y*row.Length,b=(Height-1-y)*row.Length; System.Buffer.BlockCopy(bytes,a,row,0,row.Length); System.Buffer.BlockCopy(bytes,b,bytes,a,row.Length); System.Buffer.BlockCopy(row,0,bytes,b,row.Length);}
        return bytes;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { if (Framebuffer != 0) GL.DeleteFramebuffer(Framebuffer); }
        finally
        {
            try { if (Texture != 0) GL.DeleteTexture(Texture); }
            finally { if (depth != 0) GL.DeleteRenderbuffer(depth); }
        }
    }
}

/// <summary>Preserves the original quad winding in core profiles where GL_QUADS is unavailable.</summary>
public static class LegacyQuads
{
    public static void Draw(int count, DrawElementsType type, IntPtr offset)
    {
        if(count==0)return;
        GL.GetInteger(GetPName.ElementArrayBufferBinding,out int original);
        var source=new uint[count];GL.GetBufferSubData(BufferTarget.ElementArrayBuffer,offset,count*4,source);
        var indices=new uint[count/4*6];
        for(int i=0,j=0;i+3<count;i+=4){indices[j++]=source[i];indices[j++]=source[i+1];indices[j++]=source[i+2];indices[j++]=source[i];indices[j++]=source[i+2];indices[j++]=source[i+3];}
        int scratch=GL.GenBuffer(); GL.BindBuffer(BufferTarget.ElementArrayBuffer,scratch);GL.BufferData(BufferTarget.ElementArrayBuffer,indices.Length*4,indices,BufferUsageHint.StreamDraw);
        GL.DrawElements(PrimitiveType.Triangles,indices.Length,type,IntPtr.Zero);GL.BindBuffer(BufferTarget.ElementArrayBuffer,original);GL.DeleteBuffer(scratch);
    }
}
