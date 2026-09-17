// ============================================================================
// FILE:     src/FileHarvester.App/Program.cs
// PURPOSE:  Process entry point. Configures and starts the Avalonia
//           application lifetime.
// RELATION: Called first by the OS. Creates the AppBuilder that loads
//           App.axaml.cs. Nothing else depends on this file.
// ============================================================================

using Avalonia;

namespace FileHarvester.App;

internal sealed class Program
{
    // Initialization code. Do not use any Avalonia, third-party APIs, or
    // any SynchronizationContext-reliant code before AppMain is called.
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Avalonia configuration used by the Visual Designer as well as at
    // runtime. Do not remove; also used by the previewer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
