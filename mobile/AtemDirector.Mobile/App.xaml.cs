namespace AtemDirector.Mobile;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new NavigationPage(new Pages.ServerConfigPage())
		{
			BarBackgroundColor = Color.FromArgb("#17181d"),
			BarTextColor = Colors.White
		});
	}
}