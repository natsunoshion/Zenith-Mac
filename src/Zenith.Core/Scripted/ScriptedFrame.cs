using OpenTK.Mathematics;
using ScriptedEngine;
using Font = ScriptedEngine.Font;

namespace Zenith.Core.Scripted;

/// <summary>Commands retain script order, per-vertex color/UV, and the original GL state.</summary>
public abstract record ScriptedDrawCommand;

public sealed record QuadCommand(
    Vector2d V1, Vector2d V2, Vector2d V3, Vector2d V4,
    Color4 C1, Color4 C2, Color4 C3, Color4 C4,
    Texture? Texture,
    Vector2d Uv1, Vector2d Uv2, Vector2d Uv3, Vector2d Uv4,
    TextureShaders Shader, BlendFunc Blend) : ScriptedDrawCommand;

public sealed record TextCommand(
    double Left, double Bottom, double Height, Color4 Color,
    Font Font, string Text, BlendFunc Blend) : ScriptedDrawCommand;

public sealed record FlushCommand : ScriptedDrawCommand;

public sealed class ScriptedFrame
{
    public ScriptedFrame(IReadOnlyList<ScriptedDrawCommand> commands) => Commands = commands;
    public IReadOnlyList<ScriptedDrawCommand> Commands { get; }
}
