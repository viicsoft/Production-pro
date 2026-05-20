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
            TxtProductionName.Text = $"Untitled Production - {DateTime.Now:MMM dd}";
            TxtDirectorName.Text = Environment.UserName;
        }

        private void CmbNetworkMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PnlRelayUrl == null) return; // Guard for initialization
            PnlRelayUrl.Visibility = CmbNetworkMode.SelectedIndex == 1 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }

        private void BtnCreateRoom_Click(object sender, RoutedEventArgs e)
        {
            var prodName = string.IsNullOrWhiteSpace(TxtProductionName.Text) ? "Untitled Production" : TxtProductionName.Text;
            var dirName = string.IsNullOrWhiteSpace(TxtDirectorName.Text) ? "Director" : TxtDirectorName.Text;

            var networkMode = CmbNetworkMode.SelectedIndex == 1 ? NetworkMode.Online : NetworkMode.LAN;
            var relayUrl = networkMode == NetworkMode.Online ? TxtRelayUrl.Text.Trim() : "";

            RoomManager.CreateRoom(prodName, dirName, networkMode, relayUrl);
            RoomCreated?.Invoke(this, EventArgs.Empty);
        }
    }
}
