using Avalonia;
using Bobrlog.App.Services;
using Bobrlog.Core.Service;

namespace Bobrlog.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Privileged helper mode (invoked through pkexec): no UI.
        if (args.Length > 0 && args[0] is ServiceInstaller.InstallArgument or ServiceInstaller.UninstallArgument
                or ServiceInstaller.RestartArgument)
        {
            Localization.Apply(ServiceInstaller.ArgValue(args, ServiceInstaller.LanguageArgument));
            try
            {
                Console.WriteLine(ServiceInstaller.ExecutePrivilegedAsync(args).GetAwaiter().GetResult());
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        Localization.Apply(AppSettings.Load().Language);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
