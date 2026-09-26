from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
out=root/'artifacts/ported-originals'
mods=['ClassicRender','FlatRender','PFARender','MidiTrailRender','TexturedRender','NoteCountRender']
def transform(s):
 s=re.sub(r'^using System.Windows[^;]*;\s*','',s,flags=re.M)
 s=s.replace('using OpenTK;','using OpenTK.Mathematics;').replace('using OpenTK.Graphics;','')
 s=s.replace('using System.Drawing.Imaging;','')
 s=re.sub(r'#region PreviewConvert.*?#endregion','',s,flags=re.S)
 s=re.sub(r'^.*PreviewImage = .*;\s*','',s,flags=re.M)
 s=s.replace('System.Windows.Media.ImageSource','object').replace('ImageSource','object').replace('System.Windows.Controls.Control','object')
 s=re.sub(r'\bControl\b','object',s)
 s=re.sub(r'^.*(?:SettingsControl = new SettingsCtrl|settingsControl = new SettingsCtrl|settingsCtrl = new SettingsCtrl|PaletteChanged \+=).*\n','',s,flags=re.M)
 s=s.replace('SettingsCtrl settingsControl;', '').replace('SettingsCtrl settingsCtrl;', '')
 s=s.replace('public object SettingsControl => settingsControl;', 'public object SettingsControl => settings;').replace('public object SettingsControl => settingsCtrl;', 'public object SettingsControl => settings;')
 s=s.replace('public object SettingsControl { get; private set; }','public object SettingsControl => settings;').replace('public object SettingsControl { get; set; }','public object SettingsControl => settings;')
 s=s.replace('((SettingsCtrl)SettingsControl).paletteList.GetColors(NoteColors.Length)','Zenith.Core.Rendering.PaletteService.GetColors(settings.palette, NoteColors.Length)')
 s=re.sub(r'^.*GL\.(?:EnableClientState|DisableClientState|IndexPointer)\(.*\n','',s,flags=re.M)
 s=re.sub(r'^.*GL\.(?:Enable|Disable)\(EnableCap.Texture2D\);.*\n','',s,flags=re.M)
 s=s.replace('#version 330 compatibility','#version 410 core').replace('#version 330 core','#version 410 core').replace('texture2D(', 'texture(')
 s=s.replace('out vec4 outputF;', '').replace('outputF = color;\n\ttexOut = outputF;', 'texOut = color;')
 s=s.replace('GL.DrawElements(PrimitiveType.Quads,','Zenith.Core.Rendering.LegacyQuads.Draw(')
 s=s.replace('GL.VertexPointer(2, VertexPointerType.Double, 16, 0);','GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Double, false, 16, 0);')
 s=s.replace('settingsCtrl.auraselect.SelectedImage','Zenith.Core.Rendering.PaletteService.Aura(settings.selectedAuraImage)').replace('settingsCtrl.auraselect.lastSetTime','Zenith.Core.Rendering.PaletteService.AuraRevision')
 s=s.replace('System.Drawing.FontStyle','ScriptedEngine.FontStyle')
 s=s.replace('BitmapData data = image.LockBits(new System.Drawing.Rectangle(0, 0, image.Width, image.Height),\n                ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);','var data = image.LockBits();')
 s='// Ported from arduano/Zenith-MIDI; original rendering algorithm retained. See THIRD_PARTY_NOTICES.md.\nusing Bitmap = Zenith.Core.Rendering.RasterImage;\n'+s
 return s
for m in mods:
 (out/m).mkdir(parents=True,exist_ok=True)
 for f in ['Render.cs','Settings.cs']+({'MidiTrailRender':['Util.cs'],'TexturedRender':['Pack.cs']}.get(m,[])):
  (out/m/f).write_text(transform((root/'upstream'/m/f).read_text(encoding='utf-8-sig')))
s=(root/'upstream/TexturedRender/SettingsCtrl.xaml.cs').read_text(encoding='utf-8-sig')
a=s.index('        T parseType<T>') if '        T parseType<T>' in s else s.index('        static T parseType<T>')
b=s.index('        private void PluginList_SelectionChanged',a)
s=s[a:b].replace('Pack LoadPack(', 'public Pack LoadPack(')
s=s.replace('.Where(s => s.EndsWith("\\\\pack.json"))','.Where(s => Path.GetFileName(s) == "pack.json")')
s=s.replace('path = Path.Combine(pbase, path).Replace("/", "\\\\");','path = Path.Combine(pbase, path).Replace("\\\\", "/");')
s=s.replace('a.Key == path','a.Key.Replace("\\\\", "/") == path')
preamble='using System; using System.IO; using System.Linq; using System.Collections.Generic; using System.Security.Cryptography; using System.IO.Compression; using Microsoft.CSharp.RuntimeBinder; using Newtonsoft.Json; using Newtonsoft.Json.Linq; using SharpCompress.Archives; using SharpCompress.Archives.Rar; using SharpCompress.Archives.SevenZip; using SharpCompress.Archives.Tar; using System.Drawing;\n'
(out/'TexturedRender/PackLoader.cs').write_text(transform(preamble+'namespace TexturedRender { public class PackLoader {\n'+s+'\n}}'))
