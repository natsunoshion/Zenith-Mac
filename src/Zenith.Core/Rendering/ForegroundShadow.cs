using OpenTK.Graphics.OpenGL;

namespace Zenith.Core.Rendering;

/// <summary>A shadow of the complete rendered foreground, not a note-only mask.
/// Distance and Gaussian standard deviation are measured in final output pixels.</summary>
public readonly record struct ForegroundShadowOptions(bool Enabled, double BlurPixels, double AngleDegrees,
    double DistancePixels, double Opacity)
{
    public static ForegroundShadowOptions Default => new(false, 3, 45, 18, .7);
    internal void Validate()
    {
        if (!double.IsFinite(BlurPixels) || BlurPixels is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(BlurPixels));
        if (!double.IsFinite(AngleDegrees) || AngleDegrees is < 0 or > 360)
            throw new ArgumentOutOfRangeException(nameof(AngleDegrees));
        if (!double.IsFinite(DistancePixels) || DistancePixels is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(DistancePixels));
        if (!double.IsFinite(Opacity) || Opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity));
    }
}

/// <summary>Two blur passes; SSAA first resolves coverage once instead of resampling every kernel tap.</summary>
internal sealed class ForegroundShadow : IDisposable
{
    const string Vertex = """
        #version 410 core
        out vec2 UV;
        void main() {
            vec2 p = vec2((gl_VertexID << 1) & 2, gl_VertexID & 2);
            UV = p; gl_Position = vec4(p * 2 - 1, 0, 1);
        }
        """;
    readonly RenderTarget horizontal;
    readonly RenderTarget? resolvedCoverage;
    readonly int horizontalProgram, verticalProgram, vao, sampler;
    readonly int factor;
    readonly float[] weights = new float[193];
    double lastBlur = double.NaN;
    int radius;
    bool disposed;

    internal ForegroundShadow(int width, int height, int factor)
    {
        this.factor = factor;
        try
        {
            horizontal = new RenderTarget(width, height, includeDepth: false);
            if (factor > 1) resolvedCoverage = new RenderTarget(width, height, includeDepth: false);
            horizontalProgram = ScriptedGlRenderer.Compile(Vertex, """
                #version 410 core
                in vec2 UV;
                out vec4 result;
                uniform sampler2D image;
                uniform vec2 res;
                uniform int factor;
                uniform int radius;
                uniform float offsetX;
                uniform int extractOnly;
                uniform int sourceIsCoverage;
                uniform float weights[193];
                float alphaAt(vec2 p) {
                    if (any(lessThan(p, vec2(0))) || any(greaterThan(p, vec2(1)))) return 0;
                    return texture(image, p).a;
                }
                float coverage(vec2 p) {
                    if (sourceIsCoverage != 0) return alphaAt(p);
                    // Match the alpha used to blend the original foreground
                    // onto its background, including the original SSAA path.
                    if (factor == 1) return sqrt(max(0, alphaAt(p)));
                    float value = 0;
                    for (int x = 0; x < factor; x++)
                        for (int y = 0; y < factor; y++)
                            value += alphaAt(p + vec2(x, y) / res / factor);
                    return value / (factor * factor);
                }
                void main() {
                    if (extractOnly != 0) { result = vec4(coverage(UV)); return; }
                    // Shift each axis in its own blur pass. This retains a
                    // blurred edge shifted back onto the image without needing
                    // an oversized intermediate texture or clamping edge alpha.
                    vec2 p = UV - vec2(offsetX, 0);
                    float alpha = coverage(p) * weights[0];
                    for (int i = 1; i <= radius; i++) {
                        vec2 step = vec2(i / res.x, 0);
                        alpha += (coverage(p - step) + coverage(p + step)) * weights[i];
                    }
                    result = vec4(alpha);
                }
                """);
            verticalProgram = ScriptedGlRenderer.Compile(Vertex, """
                #version 410 core
                in vec2 UV;
                out vec4 result;
                uniform sampler2D image;
                uniform vec2 res;
                uniform vec2 offset;
                uniform int radius;
                uniform float weights[193];
                uniform float opacity;
                float alphaAt(vec2 p) {
                    if (any(lessThan(p, vec2(0))) || any(greaterThan(p, vec2(1)))) return 0;
                    return texture(image, p).r;
                }
                void main() {
                    vec2 p = UV - offset;
                    float alpha = alphaAt(p) * weights[0];
                    for (int i = 1; i <= radius; i++) {
                        vec2 step = vec2(0, i / res.y);
                        alpha += (alphaAt(p - step) + alphaAt(p + step)) * weights[i];
                    }
                    result = vec4(0, 0, 0, clamp(alpha * opacity, 0, 1));
                }
                """);
            vao = GL.GenVertexArray();
            sampler = GL.GenSampler();
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.SamplerParameter(sampler, SamplerParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.SamplerParameter(sampler, SamplerParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            GL.SamplerParameter(sampler, SamplerParameterName.TextureBorderColor, new float[4]);
        }
        catch (Exception failure)
        {
            try { Dispose(); }
            catch (Exception cleanup) { failure.Data["ShadowCleanupFailure"] = cleanup; }
            throw;
        }
    }

    internal void Draw(int source, int destinationFramebuffer, ForegroundShadowOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (lastBlur != options.BlurPixels)
        {
            lastBlur = options.BlurPixels;
            radius = (int)Math.Ceiling(lastBlur * 3);
            Array.Clear(weights);
            weights[0] = 1;
            if (radius > 0)
            {
                double sigma = lastBlur;
                double total = 1;
                for (int i = 1; i <= radius; i++)
                {
                    weights[i] = (float)Math.Exp(-i * i / (2 * sigma * sigma));
                    total += weights[i] * 2;
                }
                for (int i = 0; i <= radius; i++) weights[i] /= (float)total;
            }
        }
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.GetInteger(GetPName.SamplerBinding, out int previousSampler);
        try
        {
            GL.BindSampler(0, sampler);
            GL.BindVertexArray(vao);
            GL.Viewport(0, 0, horizontal.Width, horizontal.Height);
            GL.Disable(EnableCap.Blend);
            if (resolvedCoverage != null)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, resolvedCoverage.Framebuffer);
                Configure(horizontalProgram, source);
                GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "factor"), factor);
                GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "extractOnly"), 1);
                GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "sourceIsCoverage"), 0);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, horizontal.Framebuffer);
            Configure(horizontalProgram, resolvedCoverage?.Texture ?? source);
            GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "factor"), factor);
            GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "extractOnly"), 0);
            GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "sourceIsCoverage"), resolvedCoverage == null ? 0 : 1);
            double angle = options.AngleDegrees * Math.PI / 180;
            GL.Uniform1(GL.GetUniformLocation(horizontalProgram, "offsetX"),
                (float)(Math.Cos(angle) * options.DistancePixels / horizontal.Width));
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, destinationFramebuffer);
            Configure(verticalProgram, horizontal.Texture);
            GL.Uniform2(GL.GetUniformLocation(verticalProgram, "offset"),
                0f,
                (float)(-Math.Sin(angle) * options.DistancePixels / horizontal.Height));
            GL.Uniform1(GL.GetUniformLocation(verticalProgram, "opacity"), (float)options.Opacity);
            GL.Enable(EnableCap.Blend);
            // Black straight-color shadow. Alpha uses One, so an opacity of
            // .7 darkens white by .7 and adds .7 to an empty export mask.
            GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
                BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
        finally { GL.BindSampler(0, previousSampler); }
    }

    void Configure(int program, int texture)
    {
        GL.UseProgram(program);
        GL.BindTexture(TextureTarget.Texture2D, texture);
        GL.Uniform1(GL.GetUniformLocation(program, "image"), 0);
        GL.Uniform2(GL.GetUniformLocation(program, "res"), (float)horizontal.Width, (float)horizontal.Height);
        GL.Uniform1(GL.GetUniformLocation(program, "radius"), radius);
        GL.Uniform1(GL.GetUniformLocation(program, "weights"), weights.Length, weights);
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
        if (sampler != 0) Attempt(() => GL.DeleteSampler(sampler));
        if (vao != 0) Attempt(() => GL.DeleteVertexArray(vao));
        if (horizontalProgram != 0) Attempt(() => GL.DeleteProgram(horizontalProgram));
        if (verticalProgram != 0) Attempt(() => GL.DeleteProgram(verticalProgram));
        if (horizontal != null) Attempt(horizontal.Dispose);
        if (resolvedCoverage != null) Attempt(resolvedCoverage.Dispose);
        if (failures?.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures?.Count > 1) throw new AggregateException("Failed to release foreground shadow resources.", failures);
    }
}
