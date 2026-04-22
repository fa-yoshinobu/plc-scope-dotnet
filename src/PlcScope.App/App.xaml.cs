namespace PlcScope.App;

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Infrastructure.Protocols;
using PlcScope.Infrastructure.Storage;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddSingleton<IPlcSessionFactory, PlcSessionFactory>();
        services.AddSingleton<IProjectStore, JsonProjectStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ILogStore, FileLogStore>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
