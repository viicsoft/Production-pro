using System.Configuration;
using System.Data;
using System.Windows;
using Desktop;

namespace desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnExit(ExitEventArgs e)
    {
        await PwaServer.Instance.DisposeAsync();
        base.OnExit(e);
    }
}

