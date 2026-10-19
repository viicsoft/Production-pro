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
            _config = config ?? new InputConfig();
            _switcher = switcher;

            TitleText.Text = type == DockType.SuperSource ? "SUPERSOURCE STUDIO" : "PALETTES";

            if (type == DockType.SuperSource)
            {
                DockContent.Content = new SuperSourceView(_switcher);
            }
            else if (type == DockType.Palettes)
            {
                this.MinWidth = 420;
                this.Width = 460;
                DockContent.Content = new PalettesView(_switcher);
            }

            // Load saved position safely and ensure SuperSource has enough room on screen
            var rect = GetDockRect();
            double defaultW = type == DockType.SuperSource ? 980 : 460;
            double defaultH = type == DockType.SuperSource ? 760 : 620;
            double minW = type == DockType.SuperSource ? 860 : 400;
            double minH = type == DockType.SuperSource ? 620 : 450;

            this.Width = rect.Width >= minW ? rect.Width : defaultW;
            this.Height = rect.Height >= minH ? rect.Height : defaultH;

            double screenW = SystemParameters.WorkArea.Width;
            double screenH = SystemParameters.WorkArea.Height;

            if (this.Width > screenW - 20) this.Width = Math.Max(minW, screenW - 20);
            if (this.Height > screenH - 20) this.Height = Math.Max(minH, screenH - 20);

            if (rect.X >= 0 && rect.Y >= 0 && rect.X + 100 < screenW && rect.Y + 60 < screenH)
            {
                this.Left = Math.Max(0, Math.Min(rect.X, screenW - this.Width));
                this.Top = Math.Max(0, Math.Min(rect.Y, screenH - this.Height));
            }
            else
            {
                this.Left = Math.Max(10, (screenW - this.Width) / 2.0);
                this.Top = Math.Max(10, (screenH - this.Height) / 2.0);
            }

            if (rect.IsMinimized) ToggleMinimize();
        }

        private WindowRect GetDockRect()
        {
            if (_type == DockType.SuperSource)
            {
                _config.SuperSourceDock ??= new WindowRect();
                return _config.SuperSourceDock;
            }
            else
            {
                _config.PalettesDock ??= new WindowRect();
                return _config.PalettesDock;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var rect = GetDockRect();
            rect.IsOpen = true;
            _config.Save();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            var rect = GetDockRect();
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
                try
                {
                    this.DragMove();
                    SavePosition();
                }
                catch { }
            }
        }

        private void SavePosition()
        {
            var rect = GetDockRect();
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
            var rect = GetDockRect();
            rect.IsMinimized = !rect.IsMinimized;
            
            if (rect.IsMinimized)
            {
                DockContent.Visibility = Visibility.Collapsed;
                this.MinHeight = 48;
                this.Height = 48;
                this.ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                DockContent.Visibility = Visibility.Visible;
                this.MinHeight = _type == DockType.SuperSource ? 620 : 400;
                this.Height = rect.Height >= this.MinHeight ? rect.Height : (_type == DockType.SuperSource ? 760 : 620);
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
