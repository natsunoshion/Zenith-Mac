using OpenTK.Mathematics;
using ScriptedEngine;
using Zenith.Core.Rendering;
using Zenith.Core.Scripted;
namespace ZenithEngine;
public sealed class GLTextEngine : IDisposable
{
    readonly ScriptedGlRenderer renderer=new();
    readonly Font font=new(){fontName="Arial",fontPixelSize=40};
    public void SetFont(string name,FontStyle style,int size){font.fontName=name;font.fontStyle=style;font.fontPixelSize=size;}
    public void SetFont(string name,int size)=>SetFont(name,FontStyle.Regular,size);
    public System.Drawing.SizeF GetBoundBox(string text){using var p=ScriptedGlRenderer.FontPaint(font);var lines=text.Split('\n');return new(lines.Max(x=>p.MeasureText(x)),p.FontSpacing*lines.Length);}
    public void Render(string text,Matrix4 matrix,Color4 color)
    {
        var(tex,_)=renderer.TextTexture(font,text);var size=GetBoundBox(text);
        Vector2d Point(float x,float y){var p=Vector4.TransformRow(new Vector4(x,y,0,1),matrix);return new((p.X+1)/2,(p.Y+1)/2);}
        renderer.Quad(new(Point(0,0),Point(size.Width,0),Point(size.Width,size.Height),Point(0,size.Height),color,color,color,color,tex,new(0,0),new(1,0),new(1,1),new(0,1),TextureShaders.Normal,BlendFunc.Mix));renderer.Flush();
    }
    public void Dispose()=>renderer.Dispose();
}
