using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using OpenTK.Mathematics;
using ScriptedEngine;
using SkiaSharp;
using ZenithEngine;
using Font = ScriptedEngine.Font;

namespace Zenith.Core.Scripted;

/// <summary>
/// Executes original Scripted C# packs. Only the OpenTK 3 namespace imports are
/// migrated to OpenTK 4; animation, settings callbacks, and all drawing code run as supplied.
/// </summary>
public sealed class ScriptedPack : IDisposable
{
    // IO uses global delegates in the original ABI. Serialize pack invocation so
    // preview, export, and pack loading cannot accidentally use another pack's callbacks.
    private static readonly object InvocationGate = new();
    private readonly AssemblyLoadContext assemblyContext;
    private readonly object instance;
    private readonly Type scriptType;
    private readonly Action<IEnumerable<Note>, RenderOptions> render;
    private readonly Action<RenderOptions>? renderInit;
    private readonly Action? renderDispose;
    private readonly Dictionary<Font, SKPaint> fontPaints = new();
    private bool initialized;
    private bool disposed;
    private double defaultScreenTime = 1000;
    private List<ScriptedDrawCommand>? currentCommands;
    private TextureShaders currentShader;
    private BlendFunc currentBlend;
    private double currentAspect = 16.0 / 9;

    public string Path { get; }
    public string Name { get; }
    public string Description { get; private set; } = "";
    public string SourceHash { get; }
    public byte[]? Preview { get; private set; }
    public List<Texture> Textures { get; } = new();
    public List<Font> Fonts { get; } = new();
    public IEnumerable<UISetting> SettingsUI { get; private set; } = Array.Empty<UISetting>();
    public bool UseProfiles => ReadMember("UseProfiles", false);
    public bool ManualNoteDelete => ReadMember("ManualNoteDelete", false);
    public double NoteCollectorOffset => ReadMember("NoteCollectorOffset", 0d);
    public double NoteScreenTime => ReadMember("NoteScreenTime", defaultScreenTime);
    public long LastNoteCount => ReadMember("LastNoteCount", 0L);

    /// <summary>Optional renderer metric override; returns text width divided by font height.</summary>
    public Func<Font, string, double>? TextWidthProvider { get; set; }

    private ScriptedPack(string path, ScriptedPackArchive archive)
    {
        Path = System.IO.Path.GetFullPath(path);
        Name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(archive.Source))).ToLowerInvariant();
        assemblyContext = new AssemblyLoadContext("Zenith.Script." + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = Compile(archive.Source, archive.ScriptEntry, assemblyContext);
            scriptType = assembly.GetType("Script") ?? throw new InvalidDataException("Pack must declare a public class named Script.");
            var loadMethod = scriptType.GetMethod("Load", Type.EmptyTypes)
                ?? throw new InvalidDataException("Load method required");
            var renderMethod = scriptType.GetMethod("Render", new[] { typeof(IEnumerable<Note>), typeof(RenderOptions) })
                ?? throw new InvalidDataException("Render(IEnumerable<Note>, RenderOptions) method required");

            BindLoadCallbacks(archive);
            instance = Activator.CreateInstance(scriptType)
                ?? throw new InvalidDataException("Could not construct Script");
            render = renderMethod.CreateDelegate<Action<IEnumerable<Note>, RenderOptions>>(instance);
            renderInit = scriptType.GetMethod("RenderInit", new[] { typeof(RenderOptions) })
                ?.CreateDelegate<Action<RenderOptions>>(instance);
            renderDispose = scriptType.GetMethod("RenderDispose", Type.EmptyTypes)?.CreateDelegate<Action>(instance);
            loadMethod.CreateDelegate<Action>(instance)();

            Description = ReadMember("Description", "");
            var previewPath = ReadMember<string?>("Preview", null);
            if (previewPath != null) Preview = archive.ReadAsset(previewPath);
            SettingsUI = ReadMember<IEnumerable<UISetting>>("SettingsUI", Array.Empty<UISetting>());
        }
        catch
        {
            assemblyContext.Unload();
            throw;
        }
        finally
        {
            // The archive closes after Load; do not retain it in global delegates.
            IO.loadTexture = (_, _, _) => throw new InvalidOperationException("Can't call LoadTexture outside the load function");
            IO.loadFont = (_, _, _, _) => throw new InvalidOperationException("Can't call LoadFont outside the load function");
        }
    }

    public static ScriptedPack Load(string path)
    {
        lock (InvocationGate)
        {
            using var archive = new ScriptedPackArchive(path);
            try { return new ScriptedPack(path, archive); }
            catch (TargetInvocationException error) when (error.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        }
    }

    public void Initialize(RenderOptions options)
    {
        lock (InvocationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (initialized) return;
            ValidateOptions(options);
            defaultScreenTime = options.noteScreenTime;
            currentAspect = options.renderAspectRatio;
            currentCommands = new List<ScriptedDrawCommand>();
            BindRenderCallbacks();
            try
            {
                renderInit?.Invoke(options);
                initialized = true;
            }
            finally { currentCommands = null; }
        }
    }

    public ScriptedFrame Render(IEnumerable<Note> notes, RenderOptions options)
    {
        lock (InvocationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ValidateOptions(options);
            Initialize(options);
            defaultScreenTime = options.noteScreenTime;
            currentAspect = options.renderAspectRatio;
            currentShader = TextureShaders.Normal;
            currentBlend = BlendFunc.Mix;
            currentCommands = new List<ScriptedDrawCommand>(2048);
            BindRenderCallbacks();
            try
            {
                render(notes, options);
                return new ScriptedFrame(currentCommands);
            }
            finally { currentCommands = null; }
        }
    }

    /// <summary>Resets script animation state by repeating the original render lifecycle.</summary>
    public void Reset(RenderOptions options)
    {
        lock (InvocationGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            EndRendering();
            Initialize(options);
        }
    }

    /// <summary>Ends a render session while retaining the loaded pack and editable settings.</summary>
    public void EndRendering()
    {
        lock (InvocationGate)
        {
            if (!initialized) return;
            initialized = false;
            renderDispose?.Invoke();
        }
    }

    private static void ValidateOptions(RenderOptions options)
    {
        if (options.firstKey < 0 || options.lastKey > 256 || options.firstKey >= options.lastKey)
            throw new ArgumentOutOfRangeException(nameof(options), "Key range must have 0 <= firstKey < lastKey <= 256 (lastKey is exclusive).");
        if (options.renderWidth <= 0 || options.renderHeight <= 0 || options.renderFPS <= 0 || options.noteScreenTime <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Render dimensions, FPS, and screen time must be positive.");
        options.renderAspectRatio = options.renderWidth / (double)options.renderHeight;
        options.midiTimeSignature ??= new TimeSignature();
    }

    private T ReadMember<T>(string name, T fallback)
    {
        var field = scriptType.GetField(name);
        var property = scriptType.GetProperty(name);
        var value = field != null ? field.GetValue(instance) : property?.GetValue(instance);
        return value is T typed ? typed : fallback;
    }

    private void BindLoadCallbacks(ScriptedPackArchive archive)
    {
        IO.loadTexture = (path, loop, linear) =>
        {
            var data = archive.ReadAsset(path);
            using var encoded = SKData.CreateCopy(data);
            using var codec = SKCodec.Create(encoded);
            if (codec == null) throw new InvalidDataException("Corrupt image: " + path);
            var texture = new Texture
            {
                path = path, Data = data, width = codec.Info.Width, height = codec.Info.Height,
                aspectRatio = codec.Info.Width / (double)codec.Info.Height, looped = loop, linear = linear
            };
            Textures.Add(texture);
            return texture;
        };
        IO.loadFont = (size, name, style, chars) =>
        {
            var font = new Font { fontPixelSize = size, fontName = name, fontStyle = style, charMap = chars };
            Fonts.Add(font);
            return font;
        };
    }

    private void BindRenderCallbacks()
    {
        IO.renderQuad = (left, top, right, bottom, tl, tr, br, bl, tex, ul, ut, ur, ub) =>
            AddQuad(new(left, top), new(right, top), new(right, bottom), new(left, bottom),
                tl, tr, br, bl, tex, new(ul, ut), new(ur, ut), new(ur, ub), new(ul, ub));
        IO.renderShape = AddQuad;
        IO.selectTexShader = shader => currentShader = shader;
        IO.setBlendFunc = blend => currentBlend = blend;
        IO.forceFlush = () => Commands.Add(new FlushCommand());
        IO.getTextSize = (font, text) => (TextWidthProvider?.Invoke(font, text) ?? MeasureText(font, text)) / currentAspect;
        IO.renderText = (left, bottom, height, color, font, text) =>
        {
            if (text.Contains('\n')) throw new InvalidOperationException("New line characters not allowed when rendering text (yet)");
            var filtered = font.charMap == null ? text : new string(text.Where(font.charMap.Contains).ToArray());
            Commands.Add(new TextCommand(left, bottom, height, color, font, filtered, currentBlend));
        };
    }

    private List<ScriptedDrawCommand> Commands => currentCommands
        ?? throw new InvalidOperationException("Drawing is only available during Render or RenderInit.");

    private void AddQuad(Vector2d v1, Vector2d v2, Vector2d v3, Vector2d v4,
        Color4 c1, Color4 c2, Color4 c3, Color4 c4, Texture? texture,
        Vector2d uv1, Vector2d uv2, Vector2d uv3, Vector2d uv4)
        => Commands.Add(new QuadCommand(v1, v2, v3, v4, c1, c2, c3, c4, texture,
            uv1, uv2, uv3, uv4, currentShader, currentBlend));

    private double MeasureText(Font font, string text)
    {
        if (!fontPaints.TryGetValue(font, out var paint))
        {
            var style = new SKFontStyle(
                (font.fontStyle & ScriptedEngine.FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                (font.fontStyle & ScriptedEngine.FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            paint = new SKPaint { Typeface = SKTypeface.FromFamilyName(font.fontName, style), TextSize = font.fontPixelSize, IsAntialias = true };
            fontPaints.Add(font, paint);
        }
        var filtered = font.charMap == null ? text : new string(text.Where(font.charMap.Contains).ToArray());
        // Keep IO.GetTextWidth consistent with the actual bitmap text renderer's
        // width padding and line height on this platform.
        return (Math.Ceiling(paint.MeasureText(filtered)) + 2) / Math.Ceiling(paint.FontSpacing);
    }

    private static Assembly Compile(string source, string filename, AssemblyLoadContext context)
    {
        EnforceOriginalNamespacePolicy(source);
        var tree = CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest), filename);
        var migrated = (CompilationUnitSyntax)new OpenTkNamespaceMigration().Visit(tree.GetRoot())!;
        tree = CSharpSyntaxTree.Create(migrated, (CSharpParseOptions)tree.Options, filename, Encoding.UTF8);
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { typeof(IO).Assembly.Location, typeof(Vector2d).Assembly.Location, typeof(OpenTK.Graphics.OpenGL.GL).Assembly.Location })
            .Distinct(StringComparer.Ordinal);
        var references = paths.Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("ZenithScript_" + Guid.NewGuid().ToString("N"),
            new[] { tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release, allowUnsafe: true));
        using var dll = new MemoryStream();
        var result = compilation.Emit(dll);
        if (!result.Success)
            throw new ScriptedCompilationException(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()).ToArray());
        dll.Position = 0;
        return context.LoadFromStream(dll);
    }

    private static void EnforceOriginalNamespacePolicy(string source)
    {
        // Preserve the Windows pack policy for compatibility. This textual check
        // is not a security sandbox: only load packs from sources you trust.
        var compact = string.Concat(source.Where(c => !char.IsWhiteSpace(c)));
        string[] blocked = ["System.IO", "System.Reflection", "Microsoft.CSharp", "System.Net", "Microsoft.VisualBasic",
            "System.Drawing", "System.AttributeUsage", "System.EnterpriseServices", "System.Media", "System.Messaging",
            "System.Printing", "System.Security", "System.ServiceModel", "System.ServiceProcess", "System.Speech", "System.Web",
            "System.Windows", "System.Xml", "Microsoft.Windows", "Microsoft.Win32", "Microsoft.SqlServer", "Microsoft.JScript",
            "Microsoft.Build", "Accessibility", "Microsoft.Activities", "System.Diagnostics", "System.Runtime", "System.Management"];
        foreach (var item in blocked)
            if (compact.Contains(item, StringComparison.Ordinal)) throw new InvalidDataException(item + " is not allowed");
    }

    private sealed class OpenTkNamespaceMigration : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name?.ToString() is "OpenTK" or "OpenTK.Graphics")
                return node.WithName(SyntaxFactory.ParseName("OpenTK.Mathematics").WithTriviaFrom(node.Name));
            return base.VisitUsingDirective(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var fullName = node.ToString();
            var oldPrefix = fullName.StartsWith("OpenTK.Graphics.Color4", StringComparison.Ordinal)
                ? "OpenTK.Graphics." : "OpenTK.";
            var typeName = fullName.StartsWith(oldPrefix, StringComparison.Ordinal) ? fullName[oldPrefix.Length..] : "";
            if (typeName.StartsWith("Vector", StringComparison.Ordinal) || typeName.StartsWith("Matrix", StringComparison.Ordinal)
                || typeName.StartsWith("Quaternion", StringComparison.Ordinal) || typeName is "Color4" or "MathHelper")
                return SyntaxFactory.ParseName("OpenTK.Mathematics." + typeName).WithTriviaFrom(node);
            return base.VisitQualifiedName(node);
        }
    }

    public void Dispose()
    {
        lock (InvocationGate)
        {
            if (disposed) return;
            EndRendering();
            foreach (var paint in fontPaints.Values) { paint.Typeface?.Dispose(); paint.Dispose(); }
            fontPaints.Clear();
            if (IO.renderShape?.Target == this)
            {
                IO.renderShape = null!;
                IO.renderQuad = null!;
                IO.renderText = null!;
                IO.getTextSize = null!;
                IO.selectTexShader = null!;
                IO.setBlendFunc = null!;
                IO.forceFlush = null!;
            }
            disposed = true;
            assemblyContext.Unload();
        }
    }
}

public sealed class ScriptedCompilationException : Exception
{
    public ScriptedCompilationException(IReadOnlyList<string> diagnostics)
        : base("Script compilation failed:\n" + string.Join('\n', diagnostics)) => Diagnostics = diagnostics;
    public IReadOnlyList<string> Diagnostics { get; }
}
