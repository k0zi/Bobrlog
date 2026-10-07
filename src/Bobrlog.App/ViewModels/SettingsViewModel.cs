using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bobrlog.App.Controls;
using Bobrlog.App.Resources;
using Bobrlog.App.Services;
using Bobrlog.Core.Service;

namespace Bobrlog.App.ViewModels;

public sealed partial class SettingsViewModel(AppServices services, Func<Task> sourceChanged) : PageViewModel
{
    public static readonly Avalonia.Data.Converters.IValueConverter ErrorBrushConverter =
        new Avalonia.Data.Converters.FuncValueConverter<bool, IBrush?>(isError => isError ? Brushes.IndianRed : null);

    public override string Title => Strings.Settings_Title;
    public override Geometry Icon => Icons.Tune;

    [ObservableProperty] public partial string ServiceStatusText { get; set; } = Strings.Settings_Checking;
    [ObservableProperty] public partial bool IsInstalled { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string? ResultText { get; set; }
    [ObservableProperty] public partial bool ResultIsError { get; set; }

    public bool CanInstall => !IsBusy && ServiceInstaller.IsBundledServiceAvailable;
    public bool CanRemove => !IsBusy && IsInstalled;

    public string BundleHint => ServiceInstaller.IsBundledServiceAvailable
        ? string.Format(CultureInfo.CurrentCulture, Strings.Settings_BundleHint, ServiceInstaller.InstallDirectory, ServiceInstaller.ServiceName)
        : string.Format(CultureInfo.CurrentCulture, Strings.Settings_BundleMissing, ServiceInstaller.BundledServiceDirectory);

    public string PrivilegeText
    {
        get
        {
            var p = services.Privileges;
            var who = p.IsRoot ? "root" : $"UID {p.Uid}";
            var groups = string.Join(", ", p.Groups.Where(g => g is "adm" or "systemd-journal" or "wheel" or "sudo"));
            return string.Format(CultureInfo.CurrentCulture, Strings.Settings_RunningAs, who) +
                   (groups.Length > 0 ? " • " + string.Format(CultureInfo.CurrentCulture, Strings.Settings_Groups, groups) : "") +
                   " • " + (p.CanReadSystemJournal ? Strings.Settings_FullJournal : Strings.Settings_OwnJournal);
        }
    }

    public string CurrentSourceText => string.Format(CultureInfo.CurrentCulture, Strings.Settings_CurrentSource, services.Source.DisplayName);

    public IReadOnlyList<Language> Languages => Localization.SupportedLanguages;

    public Language SelectedLanguage
    {
        get => Localization.Find(services.Settings.Language);
        set
        {
            services.Settings.Language = value.Code;
            services.Settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(NeedsRestart));
        }
    }

    /// <summary>The selected language differs from the one this instance was started with.</summary>
    public bool NeedsRestart => SelectedLanguage != Localization.Current;

    public bool UseServiceAutomatically
    {
        get => services.Settings.SourceMode == SourceMode.Auto;
        set
        {
            services.Settings.SourceMode = value ? SourceMode.Auto : SourceMode.Local;
            services.Settings.Save();
            OnPropertyChanged();
            _ = SwitchSourceAsync();
        }
    }

    public bool LowPriority
    {
        get => services.Settings.LowPriority;
        set
        {
            services.Settings.LowPriority = value;
            services.Settings.Save();
            services.ApplyProcessPriority();
            OnPropertyChanged();
        }
    }

    public decimal PageSize
    {
        get => services.Settings.PageSize;
        set
        {
            services.Settings.PageSize = (int)Math.Clamp(value, 100, 50_000);
            services.Settings.Save();
            OnPropertyChanged();
        }
    }

    public override Task ActivateAsync() => RefreshStatusAsync();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRemove));
    }

    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(CanRemove));

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        try
        {
            var status = await ServiceInstaller.GetStatusAsync();
            IsInstalled = status.Installed;
            ServiceStatusText = status.DisplayText;
            if (status.Active)
            {
                var hello = await new ServiceClient().PingAsync(TimeSpan.FromSeconds(2));
                ServiceStatusText += hello is null
                    ? " – " + Strings.Settings_SocketNoResponse
                    : " – " + string.Format(CultureInfo.CurrentCulture, Strings.Settings_Reachable, hello.Version, hello.ServiceUid);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            ServiceStatusText = string.Format(CultureInfo.CurrentCulture, Strings.Settings_StatusUnknown, ex.Message);
        }
        OnPropertyChanged(nameof(CurrentSourceText));
    }

    [RelayCommand]
    private Task InstallAsync() => RunAsync(ServiceInstaller.InstallArgument);

    [RelayCommand]
    private Task RemoveAsync() => RunAsync(ServiceInstaller.UninstallArgument);

    [RelayCommand]
    private Task RestartAsync() => RunAsync(ServiceInstaller.RestartArgument);

    private async Task RunAsync(string operation)
    {
        IsBusy = true;
        ResultText = Strings.Settings_RequestingPrivileges;
        ResultIsError = false;
        try
        {
            var (success, message) = await ServiceInstaller.RunElevatedAsync(operation);
            ResultText = message;
            ResultIsError = !success;
            if (success)
            {
                // Give the service a moment to create its socket.
                await Task.Delay(operation == ServiceInstaller.UninstallArgument ? 200 : 1500);
                await SwitchSourceAsync();
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync();
        }
    }

    /// <summary>Starts a new instance (which picks up the new language) and closes this one.</summary>
    [RelayCommand]
    private void RestartApplication()
    {
        var command = ServiceInstaller.SelfCommand();
        var psi = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (var arg in command.Skip(1).Concat(Environment.GetCommandLineArgs().Skip(1)))
            psi.ArgumentList.Add(arg);
        try
        {
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ResultText = ex.Message;
            ResultIsError = true;
            return;
        }
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private async Task SwitchSourceAsync()
    {
        await sourceChanged();
        OnPropertyChanged(nameof(CurrentSourceText));
    }
}
