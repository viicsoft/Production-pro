using System;
using System.Windows;
using System.Windows.Input;
using Core;

namespace Desktop
{
    public partial class FloatingDockWindow : Window
    {
        public enum DockType { SuperSource, Palettes }
        
        private readonly DockType _type;
        private readonly InputConfig _config;
        private readonly IAtemSwitch _switcher;

        public FloatingDockWindow(DockType type, InputConfig config, IAtemSwitch switcher)
        {
            InitializeComponent();
            _type = type;
            _config = config;
            _switcher = switcher;

            TitleText.Text = type == DockType.SuperSource ? "SUPERSOURCE" : "PALETTES";

            if (type == DockType.SuperSource)
            {
                DockContent.Content = new SuperSourceView(_switcher);
            }
            else if (type == DockType.Palettes)
            {
                DockContent.Content = new PalettesView(_switcher);
            }

            // Load saved position
            var rect = type == DockType.SuperSource ? _config.SuperSourceDock : _config.PalettesDock;
            if (rect.X >= 0 && rect.Y >= 0)
            {
                this.Left = rect.X;
                this.Top = rect.Y;
                this.Width = rect.Width;
                this.Height = rect.Height;
                if (rect.IsMinimized) ToggleMinimize();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var rect = _type == DockType.SuperSource ? _config.SuperSourceDock : _config.PalettesDock;
            rect.IsOpen = true;
            _config.Save();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            var rect = _type == DockType.SuperSource ? _config.SuperSourceDock : _config.PalettesDock;
            rect.IsOpen = false;
            _config.Save();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMinimize();
            }
            else
            {
                this.DragMove();
                SavePosition();
            }
        }

        private void SavePosition()
        {
            var rect = _type == DockType.SuperSource ? _config.SuperSourceDock : _config.PalettesDock;
            if (!rect.IsMinimized)
            {
                rect.X = this.Left;
                rect.Y = this.Top;
                rect.Width = this.Width;
                rect.Height = this.Height;
                _config.Save();
            }
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            SavePosition();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMinimize();
        }

        private void ToggleMinimize()
        {
            var rect = _type == DockType.SuperSource ? _config.SuperSourceDock : _config.PalettesDock;
            rect.IsMinimized = !rect.IsMinimized;
            
            if (rect.IsMinimized)
            {
                DockContent.Visibility = Visibility.Collapsed;
                this.MinHeight = 30;
                this.Height = 30;
                this.ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                DockContent.Visibility = Visibility.Visible;
                this.MinHeight = 100;
                this.Height = rect.Height > 30 ? rect.Height : 600;
                this.ResizeMode = ResizeMode.CanResizeWithGrip;
            }
            _config.Save();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
