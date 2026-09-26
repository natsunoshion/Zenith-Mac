using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Zenith.Mac;

/// <summary>
/// Window.Icon is a no-op in Avalonia.Native 11.3.9. Set this process's AppKit
/// application icon explicitly, including when run outside the .app bundle.
/// </summary>
internal static class MacApplicationIcon
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";

    internal static void Apply()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            using var source = AssetLoader.Open(new Uri("avares://Zenith.Mac/Assets/Zenith.icns"));
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            if (!TrySetApplicationIcon(bytes.ToArray(), out var error))
                Trace.WriteLine("Zenith application icon: " + error);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.WriteLine("Zenith application icon: " + error.Message);
        }
    }

    internal static bool TrySetApplicationIcon(byte[] bytes, out string? error)
    {
        error = null;
        if (!OperatingSystem.IsMacOS()) { error = "AppKit is only available on macOS."; return false; }
        if (pthread_main_np() == 0) { error = "AppKit icons must be set on the main thread."; return false; }
        if (bytes.Length == 0) { error = "The icon resource is empty."; return false; }
        IntPtr data = IntPtr.Zero, image = IntPtr.Zero;
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            // Avalonia has loaded AppKit and created NSApplication before this call.
            var applicationClass = objc_getClass("NSApplication");
            var imageClass = objc_getClass("NSImage");
            if (applicationClass == IntPtr.Zero || imageClass == IntPtr.Zero)
            { error = "AppKit has not been initialized."; return false; }
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            data = SendBytes(Send(objc_getClass("NSData"), Selector("alloc")),
                Selector("initWithBytes:length:"), buffer, (nuint)bytes.Length);
            image = SendArg(Send(imageClass, Selector("alloc")), Selector("initWithData:"), data);
            if (image == IntPtr.Zero || !SendBool(image, Selector("isValid")))
            { error = "AppKit could not decode the icon resource."; return false; }
            var application = Send(applicationClass, Selector("sharedApplication"));
            SendVoidArg(application, Selector("setApplicationIconImage:"), image);
            return true;
        }
        finally
        {
            if (image != IntPtr.Zero) SendVoid(image, Selector("release"));
            if (data != IntPtr.Zero) SendVoid(data, Selector("release"));
            Marshal.FreeHGlobal(buffer);
        }
    }

    static IntPtr Selector(string name) => sel_registerName(name);
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libSystem.B.dylib")] static extern int pthread_main_np();
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendArg(IntPtr receiver, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendBytes(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)] static extern bool SendBool(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendVoidArg(IntPtr receiver, IntPtr selector, IntPtr value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void SendVoid(IntPtr receiver, IntPtr selector);
}
