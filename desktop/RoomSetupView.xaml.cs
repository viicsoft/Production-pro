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
                if (RoomManager.ActiveRoom != null)
                {
                    TxtProductionName.Text = string.IsNullOrWhiteSpace(RoomManager.ActiveRoom.ProductionName)
                        ? $"Untitled Production - {DateTime.Now:MMM dd}"
                        : RoomManager.ActiveRoom.ProductionName;
                    TxtDirectorName.Text = string.IsNullOrWhiteSpace(RoomManager.ActiveRoom.DirectorName)
                        ? Environment.UserName
                        : RoomManager.ActiveRoom.DirectorName;
                    CmbNetworkMode.SelectedIndex = RoomManager.ActiveRoom.NetworkMode switch
                    {
                        NetworkMode.LAN => 0,
                        NetworkMode.Online => 1,
                        NetworkMode.Hybrid => 2,
                        _ => 1
                    };
                    if (!string.IsNullOrWhiteSpace(RoomManager.ActiveRoom.RelayUrl))
                    {
                        TxtRelayUrl.Text = RoomManager.ActiveRoom.RelayUrl;
                    }
                }
                else
                {
                    TxtProductionName.Text = $"Untitled Production - {DateTime.Now:MMM dd}";
                    TxtDirectorName.Text = Environment.UserName;
                    CmbNetworkMode.SelectedIndex = 1;
                }

                if (PnlRelayUrl != null)
                {
                    PnlRelayUrl.Visibility = (CmbNetworkMode.SelectedIndex == 1 || CmbNetworkMode.SelectedIndex == 2) 
                        ? Visibility.Visible 
                        : Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void CmbNetworkMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (PnlRelayUrl == null) return;
                PnlRelayUrl.Visibility = (CmbNetworkMode.SelectedIndex == 1 || CmbNetworkMode.SelectedIndex == 2) 
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

                var networkMode = CmbNetworkMode.SelectedIndex switch
                {
                    0 => NetworkMode.LAN,
                    1 => NetworkMode.Online,
                    2 => NetworkMode.Hybrid,
                    _ => NetworkMode.Online
                };
                var relayUrl = (networkMode == NetworkMode.Online || networkMode == NetworkMode.Hybrid) ? TxtRelayUrl.Text.Trim() : "";

                RoomManager.CreateRoom(prodName, dirName, networkMode, relayUrl);
                RoomCreated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create room: {ex.Message}", "Room Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnJoinByPin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string pin = (TxtJoinPin?.Text ?? "").Trim();
                string hint = (TxtJoinHostHint?.Text ?? "").Trim();
                if (pin.Length < 4)
                {
                    if (TxtJoinPinStatus != null) TxtJoinPinStatus.Text = "Please enter the 4-digit PIN displayed on the Host computer.";
                    return;
                }

                if (BtnJoinByPin != null) BtnJoinByPin.IsEnabled = false;
                if (TxtJoinPinStatus != null) TxtJoinPinStatus.Text = $"Searching for switcher with PIN {pin} on LAN and Cloud Relay...";

                if (Desktop.MainWindow.Instance != null)
                {
                    var (ok, msg) = await Desktop.MainWindow.Instance.ConnectRemoteSwitcherByPinAsync(pin, hint);
                    if (TxtJoinPinStatus != null) TxtJoinPinStatus.Text = msg;
                    if (ok)
                    {
                        Desktop.MainWindow.Instance.SelectViewTab("Switcher");
                    }
                }
            }
            catch (Exception ex)
            {
                if (TxtJoinPinStatus != null) TxtJoinPinStatus.Text = $"Error: {ex.Message}";
            }
            finally
            {
                if (BtnJoinByPin != null) BtnJoinByPin.IsEnabled = true;
            }
        }
    }
}
