using OpenTK.Graphics.OpenGL;
using SkiaSharp;

namespace Zenith.Core.Rendering;

/// <summary>
/// The terminal passes from upstream RenderWindow: background, SSAA, alpha
/// compensation, and optional straight-color/mask output. Call on the GL owner thread.
/// </summary>
public sealed class ScenePostProcessor : IDisposable
{
    readonly RenderTarget composite;
    readonly RenderTarget output;
    readonly int program;
    readonly int vao;
    readonly int factor;
    int background;
    bool disposed;

    public ScenePostProcessor(int width, int height, int downscale, string? backgroundPath = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(downscale);
        factor = downscale;
        try
        {
            composite = new RenderTarget(width / factor, height / factor);
            output = new RenderTarget(composite.Width, composite.Height);
            program = ScriptedGlRenderer.Compile("""
                #version 410 core
                out vec2 UV;
                uniform int flip;
                void main() {
                    vec2 position = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
                    UV = position;
                    gl_Position = vec4(position * 2 - 1, 0, 1);
                    if (flip != 0) gl_Position.y = -gl_Position.y;
                }
                """, """
                #version 410 core
                in vec2 UV;
                out vec4 color;
                uniform sampler2D image;
                uniform int mode;
                uniform int factor;
                uniform vec2 res;
                void main() {
                    if (mode == 1) {
                        // These offsets, linear filtering, and texture REPEAT
                        // deliberately match the original, including edge wrap.
                        color = vec4(0);
                        float stepX = 1 / res.x / factor;
                        float stepY = 1 / res.y / factor;
                        for (int i = 0; i < factor; i++)
                            for (int j = 0; j < factor; j++)
                                color += texture(image, UV + vec2(i * stepX, j * stepY));
                        color /= factor * factor;
                    } else {
                        color = texture(image, UV);
                        if (mode == 2) {
                            // The original writes RGB/alpha to one encoder and
                            // alpha grayscale to another. Carry the latter in A
                            // so both encoders share a single GPU readback.
                            color.rgb /= color.a;
                        } else {
                            color.a = sqrt(color.a);
                            color.rgb /= color.a;
                        }
                    }
                }
                """);
            vao = GL.GenVertexArray();
            if (backgroundPath != null && File.Exists(backgroundPath)) LoadBackground(backgroundPath);
        }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { failure.Data["PostProcessorCleanupFailure"] = cleanup; }
            throw;
        }
    }

    void LoadBackground(string path)
    {
        using var encoded = SKData.Create(path);
        using var codec = SKCodec.Create(encoded)
            ?? throw new InvalidDataException("Could not decode background image: " + path);
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height,
            SKColorType.Bgra8888, SKAlphaType.Unpremul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Could not decode background image: " + path);
        background = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, background);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
            bitmap.Width, bitmap.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
    }

    /// <returns>Top-down BGRA. In mask mode A carries the original separate mask image.</returns>
    public byte[] Render(int sourceTexture, bool alphaMask = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        GL.GetInteger(GetPName.VertexArrayBinding, out var previousVao);
        try
        {
            GL.BindVertexArray(vao);
            GL.UseProgram(program);
            GL.Disable(EnableCap.DepthTest);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.ColorMask(true, true, true, true);
            GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Uniform1(GL.GetUniformLocation(program, "image"), 0);
            GL.Uniform1(GL.GetUniformLocation(program, "factor"), factor);
            GL.Uniform2(GL.GetUniformLocation(program, "res"), (float)composite.Width, (float)composite.Height);

            Clear(composite);
            GL.Enable(EnableCap.Blend);
            if (background != 0) Draw(background, 0, flip: true);
            Draw(sourceTexture, factor > 1 ? 1 : 0);

            Clear(output);
            if (alphaMask) GL.Disable(EnableCap.Blend);
            Draw(composite.Texture, alphaMask ? 2 : 0);
            GL.Disable(EnableCap.Blend);
            return output.ReadBgra();
        }
        finally { GL.BindVertexArray(previousVao); }
    }

    static void Clear(RenderTarget target)
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, target.Framebuffer);
        GL.Viewport(0, 0, target.Width, target.Height);
        GL.ClearColor(0, 0, 0, 0);
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }

    void Draw(int texture, int mode, bool flip = false)
    {
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.Uniform1(GL.GetUniformLocation(program, "mode"), mode);
        GL.Uniform1(GL.GetUniformLocation(program, "flip"), flip ? 1 : 0);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
    }

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
        if (background != 0) Attempt(() => GL.DeleteTexture(background));
        if (vao != 0) Attempt(() => GL.DeleteVertexArray(vao));
        if (program != 0) Attempt(() => GL.DeleteProgram(program));
        if (composite != null) Attempt(composite.Dispose);
        if (output != null) Attempt(output.Dispose);
        if (failures?.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures?.Count > 1) throw new AggregateException("Failed to release the scene postprocessor.", failures);
    }
}
