using Avalonia;
using Bobrlog.App.Resources;
using Bobrlog.App.Services;
using Bobrlog.Core.Service;
using KD.Avalonia.Rice;
using KD.Avalonia.Rice.Options;
using KD.Avalonia.Rice.Theming;

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
        LocalizeRice();
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
            .LogToTrace()
            .UseRice(options =>
            {
                // AppId "bobrlog": the theme choice is saved next to settings.json (~/.config/bobrlog/rice.json).
                options.AppInfo = new RiceAppInfo
                {
                    AppId = "bobrlog",
                    Name = Strings.App_Name,
                    Author = "David Kozma",
                    Description = Strings.App_Subtitle,
                    IconUri = "avares://bobrlog/Assets/bobrlog.png",
                };
                options.SupportedThemes = BuiltInThemes.All;
                options.DefaultThemeId = BuiltInThemes.Ubuntu.Id;
            });

    private static void LocalizeRice()
    {
        RiceStrings.ToggleTheme = Strings.Rice_ToggleTheme;
        RiceStrings.Settings = Strings.Settings_Title;
        RiceStrings.About = Strings.Rice_About;
        RiceStrings.Minimize = Strings.Rice_Minimize;
        RiceStrings.Maximize = Strings.Rice_Maximize;
        RiceStrings.Restore = Strings.Rice_Restore;
        RiceStrings.Close = Strings.Rice_Close;
        RiceStrings.Author = Strings.Rice_Author;
        RiceStrings.Version = Strings.Rice_Version;
        RiceStrings.Ok = Strings.Rice_Ok;
        RiceStrings.LightVariant = Strings.Rice_LightVariant;
        RiceStrings.DarkVariant = Strings.Rice_DarkVariant;
    }
}
