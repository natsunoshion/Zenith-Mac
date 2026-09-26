using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using ScriptedEngine;
using SkiaSharp;
using Zenith.Core.Scripted;
using Font = ScriptedEngine.Font;

namespace Zenith.Core.Rendering;

public sealed class ScriptedGlRenderer : IDisposable
{
    readonly int program, buffer, indices;
    readonly Dictionary<Texture,int> textures=new();
    readonly Dictionary<string,(Texture texture,double aspect)> textTextures=new();
    readonly float[] vertices=new float[4096*4*8];
    int count; Texture? texture; TextureShaders shader; ScriptedEngine.BlendFunc blend;
    public ScriptedGlRenderer()
    {
        program=Compile("""
#version 410 core
layout(location=0) in vec2 position;
layout(location=1) in vec4 inColor;
layout(location=2) in vec2 inUv;
out vec4 color; out vec2 uv;
void main(){gl_Position=vec4(position*2-1,0,1);color=inColor;uv=inUv;}
""", """
#version 410 core
in vec4 color; in vec2 uv;
uniform sampler2D image; uniform int hasTexture; uniform int mode;
out vec4 result;
void main(){
 vec4 tex=hasTexture!=0?texture(image,uv):(mode==0?vec4(1):mode==1?vec4(0,0,0,1):vec4(0.5,0.5,0.5,1));
 if(mode==0)result=tex*color;
 else if(mode==1)result=vec4(1-(1-tex.rgb)*(1-color.rgb),tex.a*color.a);
 else {tex*=2; result=vec4(mix(tex.rgb*color.rgb,1-(2-tex.rgb)*(1-color.rgb),greaterThan(tex.rgb,vec3(1))),tex.a*color.a);}
}
""");
        buffer=GL.GenBuffer();indices=GL.GenBuffer();
        var idx=new uint[4096*6];for(uint i=0;i<4096;i++){int j=(int)i*6;uint k=i*4;idx[j]=k;idx[j+1]=k+1;idx[j+2]=k+3;idx[j+3]=k+1;idx[j+4]=k+3;idx[j+5]=k+2;}
        GL.BindBuffer(BufferTarget.ElementArrayBuffer,indices);GL.BufferData(BufferTarget.ElementArrayBuffer,idx.Length*4,idx,BufferUsageHint.StaticDraw);
    }
    public static int Compile(string vertex,string fragment)
    {
        int v=Make(ShaderType.VertexShader,vertex),f=Make(ShaderType.FragmentShader,fragment),p=GL.CreateProgram();
        GL.AttachShader(p,v);GL.AttachShader(p,f);GL.LinkProgram(p);GL.DeleteShader(v);GL.DeleteShader(f);GL.GetProgram(p,GetProgramParameterName.LinkStatus,out int status);if(status!=1)throw new InvalidOperationException(GL.GetProgramInfoLog(p));return p;
        static int Make(ShaderType type,string source){int s=GL.CreateShader(type);GL.ShaderSource(s,source);GL.CompileShader(s);GL.GetShader(s,ShaderParameter.CompileStatus,out int ok);if(ok!=1)throw new InvalidOperationException(GL.GetShaderInfoLog(s));return s;}
    }
    int GetTexture(Texture tex)
    {
        if(textures.TryGetValue(tex,out int id))return id;
        using var encoded=SKData.CreateCopy(tex.Data);
        using var codec=SKCodec.Create(encoded)??throw new InvalidDataException($"Invalid texture: {tex.path}");
        using var bitmap=new SKBitmap(codec.Info.Width,codec.Info.Height,SKColorType.Rgba8888,SKAlphaType.Unpremul);
        if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException($"Cannot decode texture: {tex.path}");
        id=GL.GenTexture();GL.BindTexture(TextureTarget.Texture2D,id);
        GL.TexImage2D(TextureTarget.Texture2D,0,PixelInternalFormat.Rgba,bitmap.Width,bitmap.Height,0,PixelFormat.Rgba,PixelType.UnsignedByte,bitmap.GetPixels());
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)(tex.linear?TextureMinFilter.Linear:TextureMinFilter.Nearest));
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMagFilter,(int)(tex.linear?TextureMagFilter.Linear:TextureMagFilter.Nearest));
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapS,(int)(tex.looped?TextureWrapMode.Repeat:TextureWrapMode.ClampToEdge));
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureWrapT,(int)(tex.looped?TextureWrapMode.Repeat:TextureWrapMode.ClampToEdge));
        textures.Add(tex,id);return id;
    }
    public void Draw(ScriptedFrame frame,double aspect)
    {
        foreach(var command in frame.Commands) switch(command){case QuadCommand q:Quad(q);break;case TextCommand t:Text(t,aspect);break;case FlushCommand:Flush();break;}Flush();
    }
    public void Quad(QuadCommand q)
    {
        if(count>0&&(texture!=q.Texture||shader!=q.Shader||blend!=q.Blend||count==4096))Flush();
        texture=q.Texture;shader=q.Shader;blend=q.Blend;
        int i=count*32;
        Add(q.V1,q.C1,q.Uv1);Add(q.V2,q.C2,q.Uv2);Add(q.V3,q.C3,q.Uv3);Add(q.V4,q.C4,q.Uv4);count++;
        void Add(Vector2d v,Color4 c,Vector2d uv){vertices[i++]=(float)v.X;vertices[i++]=(float)v.Y;vertices[i++]=c.R;vertices[i++]=c.G;vertices[i++]=c.B;vertices[i++]=c.A;vertices[i++]=(float)uv.X;vertices[i++]=(float)uv.Y;}
    }
    public void Flush()
    {
        if(count==0)return;
        GL.UseProgram(program);GL.Disable(EnableCap.DepthTest);GL.Enable(EnableCap.Blend);
        GL.BlendEquationSeparate(BlendEquationMode.FuncAdd,BlendEquationMode.FuncAdd);
        GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha,blend==ScriptedEngine.BlendFunc.Add?BlendingFactorDest.One:BlendingFactorDest.OneMinusSrcAlpha,BlendingFactorSrc.One,BlendingFactorDest.One);
        GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2D,texture==null?0:GetTexture(texture));
        GL.Uniform1(GL.GetUniformLocation(program,"hasTexture"),texture==null?0:1);GL.Uniform1(GL.GetUniformLocation(program,"mode"),(int)shader);GL.Uniform1(GL.GetUniformLocation(program,"image"),0);
        GL.BindBuffer(BufferTarget.ArrayBuffer,buffer);GL.BufferData(BufferTarget.ArrayBuffer,count*32*4,vertices,BufferUsageHint.StreamDraw);
        for(int i=0;i<3;i++)GL.EnableVertexAttribArray(i);
        GL.VertexAttribPointer(0,2,VertexAttribPointerType.Float,false,32,0);GL.VertexAttribPointer(1,4,VertexAttribPointerType.Float,false,32,8);GL.VertexAttribPointer(2,2,VertexAttribPointerType.Float,false,32,24);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer,indices);GL.DrawElements(PrimitiveType.Triangles,count*6,DrawElementsType.UnsignedInt,IntPtr.Zero);count=0;
    }
    public static SKPaint FontPaint(Font font)=>new(){Typeface=SKTypeface.FromFamilyName(font.fontName,new SKFontStyle((font.fontStyle&ScriptedEngine.FontStyle.Bold)!=0?SKFontStyleWeight.Bold:SKFontStyleWeight.Normal,SKFontStyleWidth.Normal,(font.fontStyle&ScriptedEngine.FontStyle.Italic)!=0?SKFontStyleSlant.Italic:SKFontStyleSlant.Upright)),TextSize=font.fontPixelSize,Color=SKColors.White,IsAntialias=true};
    public (Texture texture,double aspect) TextTexture(Font font,string value)
    {
        string key=$"{font.fontName}|{font.fontPixelSize}|{font.fontStyle}|{value}";
        if(textTextures.TryGetValue(key,out var cached))return cached;
        if(textTextures.Count>256){Flush();foreach(var t in textTextures.Values){if(textures.Remove(t.texture,out int id))GL.DeleteTexture(id);}textTextures.Clear();}
        using var paint=FontPaint(font);var lines=value.Split('\n');int w=Math.Max(1,(int)Math.Ceiling(lines.Max(x=>paint.MeasureText(x)))+2), h=Math.Max(1,(int)Math.Ceiling(paint.FontSpacing*lines.Length));
        using var bitmap=new SKBitmap(w,h,SKColorType.Rgba8888,SKAlphaType.Unpremul);using(var canvas=new SKCanvas(bitmap)){canvas.Clear(SKColors.Transparent);for(int i=0;i<lines.Length;i++)canvas.DrawText(lines[i],0,-paint.FontMetrics.Ascent+i*paint.FontSpacing,paint);}
        using var png=bitmap.Encode(SKEncodedImageFormat.Png,100);var tex=new Texture{path=key,Data=png.ToArray(),width=w,height=h,aspectRatio=(double)w/h,linear=true,looped=false};
        return textTextures[key]=(tex,(double)w/h);
    }
    void Text(TextCommand t,double aspect){var(tex,ratio)=TextTexture(t.Font,t.Text);double left=t.Left,right=left+t.Height*ratio/aspect,top=t.Bottom+t.Height;Quad(new(new(left,top),new(right,top),new(right,t.Bottom),new(left,t.Bottom),t.Color,t.Color,t.Color,t.Color,tex,new(0,0),new(1,0),new(1,1),new(0,1),TextureShaders.Normal,t.Blend));}
    public void Dispose(){foreach(var id in textures.Values)GL.DeleteTexture(id);textures.Clear();GL.DeleteBuffer(buffer);GL.DeleteBuffer(indices);GL.DeleteProgram(program);}
}
