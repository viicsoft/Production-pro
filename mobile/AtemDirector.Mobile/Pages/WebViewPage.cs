using Microsoft.Maui.Controls;

namespace AtemDirector.Mobile.Pages
{
    public partial class WebViewPage : ContentPage
    {
        private readonly WebView _webView;
        private readonly Button _directorBtn;
        private readonly Button _cameraBtn;
        private readonly Button _adminBtn;
        private readonly Button _settingsBtn;
        private string _serverUrl = "https://192.168.1.100:8443";

        public WebViewPage()
        {
            // Create navigation buttons
            _directorBtn = new Button
            {
                Text = "Director",
                BackgroundColor = Color.FromArgb("#1e88e5"),
                TextColor = Colors.White,
                FontSize = 12,
                Padding = new Thickness(8, 4),
                CornerRadius = 4
            };
            _directorBtn.Clicked += (s, e) => NavigateToDirector();

            _cameraBtn = new Button
            {
                Text = "Camera",
                BackgroundColor = Color.FromArgb("#333644"),
                TextColor = Colors.White,
                FontSize = 12,
                Padding = new Thickness(8, 4),
                CornerRadius = 4
            };
            _cameraBtn.Clicked += (s, e) => NavigateToCamera();

            _adminBtn = new Button
            {
                Text = "Admin",
                BackgroundColor = Color.FromArgb("#333644"),
                TextColor = Colors.White,
                FontSize = 12,
                Padding = new Thickness(8, 4),
                CornerRadius = 4
            };
            _adminBtn.Clicked += (s, e) => NavigateToAdmin();

            _settingsBtn = new Button
            {
                Text = "⚙",
                BackgroundColor = Color.FromArgb("#333644"),
                TextColor = Colors.White,
                FontSize = 14,
                Padding = new Thickness(8, 4),
                CornerRadius = 4,
                WidthRequest = 40
            };
            _settingsBtn.Clicked += OnSettingsClicked;

            // Create navigation bar
            var navBar = new HorizontalStackLayout
            {
                BackgroundColor = Color.FromArgb("#17181d"),
                Padding = new Thickness(8),
                Spacing = 8,
                Children = { _directorBtn, _cameraBtn, _adminBtn, _settingsBtn }
            };

            // Create WebView
            _webView = new WebView
            {
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            _webView.Navigating += OnNavigating;
            _webView.Navigated += OnNavigated;

            // Layout
            Content = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = GridLength.Auto },
                    new RowDefinition { Height = GridLength.Star }
                },
                Children =
                {
                    navBar,
                    _webView
                }
            };

            Grid.SetRow(navBar, 0);
            Grid.SetRow(_webView, 1);

            // Load the server URL
            LoadServerUrl();
        }

        private void LoadServerUrl()
        {
            _serverUrl = Preferences.Get("ServerUrl", "https://192.168.1.100:8443");
            NavigateToDirector();
        }

        private void NavigateToDirector()
        {
            _webView.Source = _serverUrl + "/";
            UpdateButtonStates(_directorBtn);
        }

        private void NavigateToCamera()
        {
            _webView.Source = _serverUrl + "/";
            UpdateButtonStates(_cameraBtn);
        }

        private void NavigateToAdmin()
        {
            _webView.Source = _serverUrl + "/Admin/Index";
            UpdateButtonStates(_adminBtn);
        }

        private void UpdateButtonStates(Button activeButton)
        {
            var activeColor = Color.FromArgb("#1e88e5");
            var inactiveColor = Color.FromArgb("#333644");

            _directorBtn.BackgroundColor = inactiveColor;
            _cameraBtn.BackgroundColor = inactiveColor;
            _adminBtn.BackgroundColor = inactiveColor;

            activeButton.BackgroundColor = activeColor;
        }

        private async void OnSettingsClicked(object? sender, EventArgs e)
        {
            var action = await DisplayActionSheet("Settings", "Cancel", null, "Change Server URL", "Reload Page");
            
            if (action == "Change Server URL")
            {
                await Navigation.PopAsync();
            }
            else if (action == "Reload Page")
            {
                _webView.Reload();
            }
        }

        private void OnNavigating(object? sender, WebNavigatingEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Navigating to: {e.Url}");
        }

        private void OnNavigated(object? sender, WebNavigatedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Navigated to: {e.Url} - Result: {e.Result}");
            
            if (e.Result != WebNavigationResult.Success)
            {
                DisplayAlert("Connection Error", 
                    "Could not connect to the server. Please check your server URL and network connection.", 
                    "OK");
            }
        }

        public void SetServerUrl(string url)
        {
            _serverUrl = url;
            Preferences.Set("ServerUrl", url);
            NavigateToDirector();
        }
    }
}
