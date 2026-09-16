using System;
using System.Windows;
using System.Windows.Controls;

namespace Desktop
{
    public partial class RoomSetupView : UserControl
    {
        public event EventHandler? RoomCreated;

        public RoomSetupView()
        {
            InitializeComponent();
            try
            {
                TxtProductionName.Text = $"Untitled Production - {DateTime.Now:MMM dd}";
                TxtDirectorName.Text = Environment.UserName;
            }
            catch { }
        }

        private void CmbNetworkMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (PnlRelayUrl == null) return;
                PnlRelayUrl.Visibility = CmbNetworkMode.SelectedIndex == 1 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
            catch { }
        }

        private void BtnCreateRoom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var prodName = string.IsNullOrWhiteSpace(TxtProductionName.Text) ? "Untitled Production" : TxtProductionName.Text;
                var dirName = string.IsNullOrWhiteSpace(TxtDirectorName.Text) ? "Director" : TxtDirectorName.Text;

                var networkMode = CmbNetworkMode.SelectedIndex == 1 ? NetworkMode.Online : NetworkMode.LAN;
                var relayUrl = networkMode == NetworkMode.Online ? TxtRelayUrl.Text.Trim() : "";

                RoomManager.CreateRoom(prodName, dirName, networkMode, relayUrl);
                RoomCreated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create room: {ex.Message}", "Room Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
