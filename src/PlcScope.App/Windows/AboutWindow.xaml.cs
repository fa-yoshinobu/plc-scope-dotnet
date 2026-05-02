namespace PlcScope.App.Windows;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PlcScope.Core.Services;
using PlcScope.Infrastructure.Protocols;

internal sealed partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var appVersion = GetAssemblyVersionText(Assembly.GetExecutingAssembly());
        VersionTextBlock.Text = $"Version: {appVersion}";

        LibrariesListView.ItemsSource = new[]
        {
            new LibraryInfo("PLC Scope", appVersion, "MIT", "Application itself"),
            new LibraryInfo("PlcScope.Core", GetAssemblyVersionText(typeof(ProtocolCatalog).Assembly), "MIT", "Core models and services"),
            new LibraryInfo("PlcScope.Infrastructure", GetAssemblyVersionText(typeof(PlcSessionFactory).Assembly), "MIT", "PLC communication adapters"),
            new LibraryInfo("CommunityToolkit.Mvvm", GetAssemblyVersionText(typeof(ObservableObject).Assembly), "MIT", "MVVM helpers"),
            new LibraryInfo("Microsoft.Extensions.DependencyInjection", GetAssemblyVersionText(typeof(ServiceCollection).Assembly), "MIT", "Dependency injection"),
            new LibraryInfo("PlcComm.Slmp", GetAssemblyVersionText("PlcComm.Slmp"), "See package", "MELSEC SLMP communication"),
            new LibraryInfo("PlcComm.KvHostLink", GetAssemblyVersionText("PlcComm.KvHostLink"), "See package", "KEYENCE Host Link communication"),
            new LibraryInfo("PlcComm.Toyopuc", GetAssemblyVersionText("PlcComm.Toyopuc"), "See package", "TOYOPUC Computer Link communication"),
            new LibraryInfo(".NET Runtime", Environment.Version.ToString(), "MIT", "Application runtime"),
        };

        LicenseTextBox.Text = LoadLicenseText();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (InvalidOperationException)
        {
            // Ignore browser launch failures.
        }
        catch (Win32Exception)
        {
            // Ignore browser launch failures.
        }

        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string GetAssemblyVersionText(string assemblyName)
    {
        try
        {
            return GetAssemblyVersionText(Assembly.Load(new AssemblyName(assemblyName)));
        }
        catch (FileNotFoundException)
        {
            return "Unknown";
        }
        catch (FileLoadException)
        {
            return "Unknown";
        }
        catch (BadImageFormatException)
        {
            return "Unknown";
        }
    }

    private static string GetAssemblyVersionText(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plusIndex = info.IndexOf('+', StringComparison.Ordinal);
            return plusIndex >= 0 ? info[..plusIndex] : info;
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? "Unknown";
    }

    private static string LoadLicenseText()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("PlcScope.App.LICENSE");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }
        catch (IOException)
        {
            // Ignore and fall back below.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore and fall back below.
        }
        catch (NotSupportedException)
        {
            // Ignore and fall back below.
        }

        return "Embedded LICENSE resource was not found.";
    }

    private sealed record LibraryInfo(string Name, string Version, string License, string Notes);
}
