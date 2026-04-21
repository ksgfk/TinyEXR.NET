using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TinyEXR.Viewer.ViewModels;

namespace TinyEXR.Viewer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? initialPath = desktop.Args?.FirstOrDefault(static arg => !string.IsNullOrWhiteSpace(arg));
            desktop.MainWindow = new MainWindow(new MainWindowViewModel(), initialPath);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
