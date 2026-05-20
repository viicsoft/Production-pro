using Microsoft.Maui.Controls;

namespace AtemDirector.Mobile.Pages
{
    public partial class ServerConfigPage : ContentPage
    {
        private readonly Entry _serverUrlEntry;
        private readonly Button _connectButton;

        public ServerConfigPage()
        {
            Title = "AtemDirector Setup";
            BackgroundColor = Color.FromArgb("#101014");

            _serverUrlEntry = new Entry
            {
                Placeholder = "Enter server URL (e.g., https://192.168.1.100:8443)",
                Text = Preferences.Get("ServerUrl", "https://192.168.1.100:8443"),
                Keyboard = Keyboard.Url,
                TextColor = Colors.White,
                PlaceholderColor = Colors.Gray,
                BackgroundColor = Color.FromArgb("#17181d"),
                Margin = new Thickness(20, 10)
            };

            _connectButton = new Button
            {
                Text = "Connect",
                BackgroundColor = Color.FromArgb("#1e88e5"),
                TextColor = Colors.White,
                Margin = new Thickness(20, 10),
                CornerRadius = 8
            };

            _connectButton.Clicked += OnConnectClicked;

            Content = new StackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "AtemDirector",
                        FontSize = 32,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 0, 20)
                    },
                    new Label
                    {
                        Text = "Enter your AtemDirector server URL",
                        FontSize = 16,
                        TextColor = Colors.LightGray,
                        HorizontalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 0, 30)
                    },
                    _serverUrlEntry,
                    _connectButton
                }
            };
        }

        private async void OnConnectClicked(object? sender, EventArgs e)
        {
            var url = _serverUrlEntry.Text?.Trim();
            
            if (string.IsNullOrEmpty(url))
            {
                await DisplayAlert("Error", "Please enter a server URL", "OK");
                return;
            }

            // Validate URL format
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                await DisplayAlert("Error", "Invalid URL format", "OK");
                return;
            }

            // Save the URL
            Preferences.Set("ServerUrl", url);

            // Navigate to WebView page
            var webViewPage = new WebViewPage();
            webViewPage.SetServerUrl(url);
            await Navigation.PushAsync(webViewPage);
        }
    }
}
