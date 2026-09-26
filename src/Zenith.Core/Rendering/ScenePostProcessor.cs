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
    string backgroundPath = "";
    string loadedBackgroundPath = "";
    double backgroundOpacity = 1;
    double backgroundPositionX = .5;
    double backgroundPositionY = .5;
    int backgroundWidth;
    int backgroundHeight;
    bool backgroundEnabled;
    ForegroundShadowOptions shadow = ForegroundShadowOptions.Default;
    ForegroundShadow? shadowRenderer;
    RenderTarget? shadowBaseline;
    bool premultipliedForeground;
    bool disposed;

    public ScenePostProcessor(int width, int height, int downscale, string? backgroundPath = null, double backgroundOpacity = 1)
        : this(width, height, downscale, backgroundPath, backgroundOpacity, .5, .5) { }

    public ScenePostProcessor(int width, int height, int downscale, string? backgroundPath, double backgroundOpacity,
        double backgroundPositionX, double backgroundPositionY)
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
                uniform sampler2D originalImage;
                uniform int mode;
                uniform int flip;
                uniform int factor;
                uniform vec2 res;
                uniform float backgroundOpacity;
                uniform vec4 backgroundUv;
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
                    } else if (mode >= 6) {
                        vec2 sampleUv = flip != 0 ? UV * backgroundUv.xy + backgroundUv.zw : UV;
                        color = texture(image, sampleUv);
                        if (mode == 8) {
                            // Uploaded images are straight RGBA; convert once
                            // before placing them below a premultiplied scene.
                            color.a *= backgroundOpacity;
                            color.rgb *= color.a;
                        } else if (mode == 7) {
                            // A separate mask needs straight RGB. Additive light
                            // can exceed coverage; normalized 8-bit output may
                            // clip such values, as in the original mask format.
                            color.rgb = color.a > 0 ? color.rgb / color.a : vec3(0);
                        }
                    } else if (mode >= 4) {
                        vec4 original = texture(originalImage, UV);
                        vec4 shaded = texture(image, UV);
                        // Keep the unshadowed terminal RGB clamp. The new alpha
                        // includes shadow opacity; using it for sqrt compensation
                        // would raise that clamp and brighten legacy note edges.
                        // Cs already contains the unchanged foreground plus the
                        // remaining background. Capping it at the old limit
                        // neither brightens the image nor subtracts foreground.
                        vec3 legacyRgb = min(original.rgb, vec3(sqrt(original.a)));
                        vec3 rgb = min(legacyRgb, shaded.rgb);
                        if (mode == 5) rgb = shaded.a > 0 ? rgb / shaded.a : vec3(0);
                        color = vec4(rgb, shaded.a);
                    } else {
                        // Only the uploaded PNG is flipped. Its UV rectangle
                        // implements aspect-preserving cover, before the legacy
                        // alpha compensation and background opacity passes.
                        vec2 sampleUv = flip != 0 ? UV * backgroundUv.xy + backgroundUv.zw : UV;
                        color = texture(image, sampleUv);
                        if (mode == 2) {
                            // The original writes RGB/alpha to one encoder and
                            // alpha grayscale to another. Carry the latter in A
                            // so both encoders share a single GPU readback.
                            color.rgb /= color.a;
                        } else {
                            color.a = sqrt(color.a);
                            color.rgb /= color.a;
                            if (mode == 3) {
                                // Preserve the original compensated PNG pass at
                                // 100%. GL otherwise clamps this RGB immediately
                                // before blending. Scale both terms by sqrt(p)
                                // so the resulting RGB and PNG alpha become p
                                // times their original values, not p squared.
                                float opacity = sqrt(backgroundOpacity);
                                color.rgb = clamp(color.rgb, 0, 1) * opacity;
                                color.a *= opacity;
                            }
                        }
                    }
                }
                """);
            vao = GL.GenVertexArray();
            SetBackground(backgroundPath, backgroundOpacity, backgroundPositionX, backgroundPositionY);
        }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { failure.Data["PostProcessorCleanupFailure"] = cleanup; }
            throw;
        }
    }

    /// <summary>Update only the background. Call on the owning GL thread.</summary>
    public bool SetBackground(string? path, double opacity)
        => SetBackground(path, opacity, backgroundPositionX, backgroundPositionY);

    /// <summary>Position ranges from left/top (0) to right/bottom (1), with a centered crop at .5.</summary>
    public bool SetBackground(string? path, double opacity, double positionX, double positionY)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        if (!double.IsFinite(positionX) || positionX is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(positionX));
        if (!double.IsFinite(positionY) || positionY is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(positionY));
        path ??= "";
        if (path == backgroundPath && opacity == backgroundOpacity
            && positionX == backgroundPositionX && positionY == backgroundPositionY) return false;
        bool enabled = !string.IsNullOrWhiteSpace(path)
            && (background != 0 && loadedBackgroundPath == path || File.Exists(path));
        if (enabled && (background == 0 || loadedBackgroundPath != path))
        {
            // Decode/upload before releasing the old texture. Disabling retains
            // one cached image so toggling it on again does not reread the file.
            GL.GetInteger(GetPName.ActiveTexture, out int previousActive);
            GL.GetInteger(GetPName.TextureBinding2D, out int previousBinding);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.GetInteger(GetPName.TextureBinding2D, out int previousUnit0Binding);
            int oldTexture = background;
            try
            {
                background = LoadBackground(path, out int width, out int height);
                backgroundWidth = width;
                backgroundHeight = height;
                if (oldTexture != 0) GL.DeleteTexture(oldTexture);
                loadedBackgroundPath = path;
            }
            finally
            {
                // Preserve the module's texture unit/binding. A deleted old
                // background cannot be rebound: substitute its replacement.
                int Restore(int binding) => oldTexture != 0 && binding == oldTexture ? background : binding;
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, Restore(previousUnit0Binding));
                GL.ActiveTexture((TextureUnit)previousActive);
                GL.BindTexture(TextureTarget.Texture2D, Restore(previousBinding));
            }
        }
        backgroundPath = path;
        backgroundOpacity = opacity;
        backgroundPositionX = positionX;
        backgroundPositionY = positionY;
        backgroundEnabled = enabled;
        return true;
    }

    static int LoadBackground(string path, out int width, out int height)
    {
        using var encoded = SKData.Create(path);
        using var codec = SKCodec.Create(encoded)
            ?? throw new InvalidDataException("Could not decode background image: " + path);
        using var bitmap = new SKBitmap(codec.Info.Width, codec.Info.Height,
            SKColorType.Bgra8888, SKAlphaType.Unpremul);
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Could not decode background image: " + path);
        width = bitmap.Width;
        height = bitmap.Height;
        int texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                bitmap.Width, bitmap.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return texture;
        }
        catch
        {
            GL.DeleteTexture(texture);
            throw;
        }
    }

    public bool SetShadow(ForegroundShadowOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        options.Validate();
        if (shadow == options) return false;
        shadow = options;
        return true;
    }

    /// <summary>
    /// Use true source-over coverage and premultiplied RGB supplied by the
    /// corrected Scripted renderer. The default retains the builtin/legacy
    /// terminal passes. Change only on the owning GL thread.
    /// </summary>
    public bool SetPremultipliedForeground(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (premultipliedForeground == enabled) return false;
        premultipliedForeground = enabled;
        return true;
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
            SetForegroundBlend();
            GL.Uniform1(GL.GetUniformLocation(program, "image"), 0);
            GL.Uniform1(GL.GetUniformLocation(program, "factor"), factor);
            GL.Uniform2(GL.GetUniformLocation(program, "res"), (float)composite.Width, (float)composite.Height);
            GL.Uniform1(GL.GetUniformLocation(program, "backgroundOpacity"), (float)backgroundOpacity);
            double imageAspect = backgroundHeight > 0 ? (double)backgroundWidth / backgroundHeight : 1;
            double frameAspect = (double)composite.Width / composite.Height;
            double scaleX = Math.Min(1, frameAspect / imageAspect);
            double scaleY = Math.Min(1, imageAspect / frameAspect);
            GL.Uniform4(GL.GetUniformLocation(program, "backgroundUv"), (float)scaleX, (float)scaleY,
                (float)((1 - scaleX) * backgroundPositionX), (float)((1 - scaleY) * backgroundPositionY));

            Clear(composite);
            GL.Enable(EnableCap.Blend);
            if (backgroundEnabled && background != 0 && backgroundOpacity > 0)
                Draw(background, premultipliedForeground ? 8 : backgroundOpacity == 1 ? 0 : 3, flip: true);
            if (shadow.Enabled && shadow.Opacity > 0)
            {
                if (!premultipliedForeground)
                {
                    shadowBaseline ??= new(composite.Width, composite.Height, includeDepth: false);
                    GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, composite.Framebuffer);
                    GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, shadowBaseline.Framebuffer);
                    GL.BlitFramebuffer(0, 0, composite.Width, composite.Height, 0, 0, composite.Width, composite.Height,
                        ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, shadowBaseline.Framebuffer);
                    Draw(sourceTexture, factor > 1 ? 1 : 0);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, composite.Framebuffer);
                }
                shadowRenderer ??= new(composite.Width, composite.Height, factor);
                shadowRenderer.Draw(sourceTexture, composite.Framebuffer, shadow, premultipliedForeground);
                GL.BindVertexArray(vao);
                GL.UseProgram(program);
                SetForegroundBlend();
            }
            // SSAA averages premultiplied RGB and coverage together, before
            // blending, so faint particles do not get multiplied by alpha twice.
            Draw(sourceTexture, factor > 1 ? 1 : premultipliedForeground ? 6 : 0);

            Clear(output);
            if (premultipliedForeground)
            {
                GL.Disable(EnableCap.Blend);
                Draw(composite.Texture, alphaMask ? 7 : 6);
            }
            else if (shadow.Enabled && shadow.Opacity > 0)
            {
                GL.Disable(EnableCap.Blend);
                GL.ActiveTexture(TextureUnit.Texture1);
                GL.GetInteger(GetPName.TextureBinding2D, out int previousTexture);
                GL.GetInteger(GetPName.SamplerBinding, out int previousSampler);
                try
                {
                    GL.BindTexture(TextureTarget.Texture2D, shadowBaseline!.Texture);
                    GL.BindSampler(1, 0);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.Uniform1(GL.GetUniformLocation(program, "originalImage"), 1);
                    Draw(composite.Texture, alphaMask ? 5 : 4);
                }
                finally
                {
                    GL.ActiveTexture(TextureUnit.Texture1);
                    GL.BindTexture(TextureTarget.Texture2D, previousTexture);
                    GL.BindSampler(1, previousSampler);
                    GL.ActiveTexture(TextureUnit.Texture0);
                }
            }
            else
            {
                if (alphaMask) GL.Disable(EnableCap.Blend);
                Draw(composite.Texture, alphaMask ? 2 : 0);
            }
            GL.Disable(EnableCap.Blend);
            return output.ReadBgra();
        }
        finally { GL.BindVertexArray(previousVao); }
    }

    void SetForegroundBlend()
    {
        if (premultipliedForeground)
            GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        else
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
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
        if (shadowRenderer != null) Attempt(shadowRenderer.Dispose);
        if (shadowBaseline != null) Attempt(shadowBaseline.Dispose);
        if (vao != 0) Attempt(() => GL.DeleteVertexArray(vao));
        if (program != 0) Attempt(() => GL.DeleteProgram(program));
        if (composite != null) Attempt(composite.Dispose);
        if (output != null) Attempt(output.Dispose);
        if (failures?.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures?.Count > 1) throw new AggregateException("Failed to release the scene postprocessor.", failures);
    }
}
