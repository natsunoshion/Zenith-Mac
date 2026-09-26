using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
namespace Zenith.Mac;
public partial class App : Application
{
    AppController? controller;
    public override void Initialize()=>AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MacApplicationIcon.Apply();
            desktop.Startup += (_, _) => MacApplicationIcon.Apply();
            var window=new MainWindow();controller=new(window);desktop.MainWindow=window;desktop.Exit+=(_,_)=>controller.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
