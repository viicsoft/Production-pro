using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Core;
using Desktop.HardwareAbstraction;

namespace Desktop
{
    public partial class AudioMixerView : UserControl
    {
        private readonly IAtemSwitch _switcher;
        private readonly AudioControlService _audioService;
        private readonly DispatcherTimer _meterTimer;
        private readonly List<ChannelStripController> _stripControllers = new();
                private readonly Random _rnd = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, (double L, double R)> _liveAudioLevels = new();

        private readonly System.Windows.Media.Brush _meterActiveBrush = CreateMeterGradient();
        private readonly System.Windows.Media.Brush _meterInactiveBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));

        public AudioMixerView(IAtemSwitch switcher, AudioControlService audioService)
        {
            InitializeComponent();
            _switcher = switcher;
            _audioService = audioService;

            BuildMixerSurfaces();

            _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // 30 FPS
                        _meterTimer.Tick += MeterTimer_Tick;
            _meterTimer.Start();
            
                        _audioService.MeterChanged += OnAudioServiceMeterChanged;
            _audioService.StateChanged += OnAudioStateChanged;

            Unloaded += (s, e) => {
                _meterTimer.Stop();
                                _audioService.MeterChanged -= OnAudioServiceMeterChanged;
                _audioService.StateChanged -= OnAudioStateChanged;
            };
        }

        private void BuildMixerSurfaces()
        {
            FaderPanel.Children.Clear();
            _stripControllers.Clear();

            var config = MainWindow.Instance?.GetConfig() ?? InputConfig.Load();

            // 1. Camera Channels (CAM1 .. CAM40 or Active Inputs)
            var inputs = config.ActiveInputs != null && config.ActiveInputs.Count > 0
                ? config.ActiveInputs.OrderBy(x => x).ToList()
                : Enumerable.Range(1, 40).ToList();

            foreach (var inputId in inputs)
            {
                string label = config.GetLabel(inputId);
                var strip = CreateFairlightChannelStrip(inputId, $"CAM{inputId}", label, false);
                FaderPanel.Children.Add(strip);
            }

            // Divider
            FaderPanel.Children.Add(CreateDivider());

            // 2. Media Player Strips (Media 1 to 4)
            for (int m = 1; m <= 4; m++)
            {
                long mediaId = LogicalAudioChannels.Media(m);
                FaderPanel.Children.Add(CreateFairlightChannelStrip((int)mediaId, $"MP {m}", $"Media {m}", false));
            }

            // Divider
            FaderPanel.Children.Add(CreateDivider());

            // 3. XLR / Analog Audio Inputs (XLR 1, XLR 2)
            FaderPanel.Children.Add(CreateFairlightChannelStrip((int)LogicalAudioChannels.Xlr1, "MIC 1", "XLR In 1", false));
            FaderPanel.Children.Add(CreateFairlightChannelStrip((int)LogicalAudioChannels.Xlr2, "MIC 2", "XLR In 2", false));

            // Divider
            FaderPanel.Children.Add(CreateDivider());

            // 4. MADI Digital Audio Inputs (MADI 1 to 32)
            for (int m = 1; m <= 32; m++)
            {
                long madiId = LogicalAudioChannels.Madi(m);
                FaderPanel.Children.Add(CreateFairlightChannelStrip((int)madiId, $"MADI {m}", $"MADI {m}", false));
            }

            // 6. Master Channel Strip (Pinned to the right)
            var masterStrip = CreateMasterStrip();
            MasterPanelContainer.Child = masterStrip;
        }

        private static Border CreateDivider(Color? color = null)
        {
            return new Border
            {
                Background = new SolidColorBrush(color ?? Color.FromRgb(0x28, 0x28, 0x32)),
                Width = 1.5,
                Margin = new Thickness(4, 4, 4, 4)
            };
        }

        #region Channel Strip Controller & State

        private sealed class ChannelStripController
        {
            public int InputId;
            public bool IsMaster;
            public AudioGroupConfig? GroupConfig;
            public ChannelAudioSettings Settings = null!;

            public Border TallyBar = null!;
            public TextBlock TitleText = null!;

            public Canvas EqCanvas = null!;
            public Canvas DynCanvas = null!;
            public TextBlock TxtPeakDb = null!;

            public Slider FaderSlider = null!;
            public TextBlock TxtFaderDb = null!;

            public Border MeterFillL = null!;
            public Border MeterFillR = null!;
            public Border MeterPeakTickL = null!;
            public Border MeterPeakTickR = null!;

            public Button BtnAfv = null!;
            public Button BtnOn = null!;
            public Button BtnOff = null!;
            public Button BtnSolo = null!;

            public double CurrentLevelL = 0.0;
            public double CurrentLevelR = 0.0;
            public double PeakLevelL = 0.0;
            public double PeakLevelR = 0.0;
            public int PeakHoldTicksL = 0;
            public int PeakHoldTicksR = 0;

            public Action RedrawEq = null!;
            public Action RedrawDyn = null!;
        }

        #endregion

        #region Channel Strip Builder

        private UIElement CreateFairlightChannelStrip(int inputId, string shortTag, string labelName, bool isMaster, AudioGroupConfig? grp = null)
        {
            var config = MainWindow.Instance?.GetConfig() ?? InputConfig.Load();
            var settings = config.GetChannelAudio(inputId);

            var ctrl = new ChannelStripController
            {
                InputId = inputId,
                IsMaster = isMaster,
                GroupConfig = grp,
                Settings = settings
            };

            var stripBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x30)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Width = 102,
                Margin = new Thickness(2, 4, 2, 4),
                Padding = new Thickness(4, 4, 4, 4)
            };

            var stripStack = new StackPanel();

            // 1. Top Tally Status Bar
            ctrl.TallyBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x44)),
                Margin = new Thickness(4, 0, 4, 3)
            };
            stripStack.Children.Add(ctrl.TallyBar);

            // 2. Channel Title (CAM1, SoBe, ApTV, etc.)
            ctrl.TitleText = new TextBlock
            {
                Text = shortTag,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stripStack.Children.Add(ctrl.TitleText);

            // 3. Input Gain Knob Section (Single central metallic knob with -inf .. +6 and readout)
            var inputContainer = new Border
            {
                Height = 82,
                Background = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x16)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(2, 2, 2, 2)
            };

            var gainKnob = CreateRotaryKnob(
                minLabel: "-∞",
                maxLabel: "+6",
                initialValue: settings.InputGainL,
                min: -60.0,
                max: 6.0,
                unit: "dB",
                arcColor: Color.FromRgb(0x4C, 0xAF, 0x50),
                showDelay: true,
                onValueChanged: val =>
                {
                    settings.InputGainL = val;
                    settings.InputGainR = val;
                    config.Save();
                }
            );
            var inputStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var sourceLabel = new TextBlock
            {
                Text = labelName,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x94)),
                FontSize = 8,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            inputStack.Children.Add(sourceLabel);
            inputStack.Children.Add(gainKnob);
            inputContainer.Child = inputStack;
            stripStack.Children.Add(inputContainer);

            // 4. Equalizer ("Wave Adjuster" Mini Curve Box)
            var eqContainer = new Button { Height = 44, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x13)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, ToolTip = "Click to open Fairlight 6-Band Parametric Equalizer", Tag = "interactive", Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Style = (Style)this.FindResource("InvisibleButtonStyle") ?? new Style() };
            ctrl.EqCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = "interactive"
            };
            eqContainer.Content = ctrl.EqCanvas;

            ctrl.RedrawEq = () => DrawMiniEqCurve(ctrl.EqCanvas, settings.Eq);
            ctrl.EqCanvas.Loaded += (s, e) => ctrl.RedrawEq();

            eqContainer.Click += (s, e) => { OpenEqWindow(inputId, $"{shortTag} ({labelName})"); };
            stripStack.Children.Add(eqContainer);

            // 5. Dynamics ("Wave Adjuster" Mini Transfer Curve Box)
            var dynContainer = new Button { Height = 44, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x13)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, ToolTip = "Click to open Fairlight Dynamics (Gate, Compressor, Limiter)", Tag = "interactive", Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Style = (Style)this.FindResource("InvisibleButtonStyle") ?? new Style() };
            ctrl.DynCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = "interactive"
            };
            dynContainer.Content = ctrl.DynCanvas;

            ctrl.RedrawDyn = () => DrawMiniDynCurve(ctrl.DynCanvas, settings.Dynamics);
            ctrl.DynCanvas.Loaded += (s, e) => ctrl.RedrawDyn();

            dynContainer.Click += (s, e) => { OpenDynWindow(inputId, $"{shortTag} ({labelName})"); };
            stripStack.Children.Add(dynContainer);

            // 6. dB Peak Numeric Readout Row
            ctrl.TxtPeakDb = new TextBlock
            {
                Text = "-INF",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stripStack.Children.Add(ctrl.TxtPeakDb);

            // 7. Fader Track, Exact Scale & Twin Stereo Meters (Matches reference image)
            var faderAreaGrid = new Grid
            {
                Height = 188,
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            faderAreaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); // Fader Slider
            faderAreaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) }); // Scale Ticks & Numbers
            faderAreaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); // Dual Meter Bars + L/R

            // Vertical Fader Slider with authentic metallic silver cap
            ctrl.FaderSlider = new Slider
            {
                Style = (Style)FindResource("FairlightFaderStyle"),
                Orientation = Orientation.Vertical,
                Minimum = -60,
                Maximum = 10,
                Value = settings.FaderLevel,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = "interactive"
            };

            ctrl.FaderSlider.ValueChanged += async (s, e) =>
            {
                double val = e.NewValue;
                settings.FaderLevel = val;
                ctrl.TxtFaderDb.Text = val <= -59 ? "-INF" : $"{val:+0.00;-0.00;0.00}";

                try
                {
                    if (ctrl.GroupConfig != null)
                    {
                        ctrl.GroupConfig.Level = val;
                        foreach (var ch in ctrl.GroupConfig.Channels)
                        {
                            await _audioService.SetVolumeAsync(ch, val);
                        }
                    }
                    else
                    {
                        await _audioService.SetVolumeAsync(inputId, val);
                    }
                }
                catch { }
            };
            Grid.SetColumn(ctrl.FaderSlider, 0);
            faderAreaGrid.Children.Add(ctrl.FaderSlider);

            // Scale Labels (0, -5, -10, -15, -20, -30, -40, -50)
            var scaleGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 16, 2, 16)
            };
            scaleGrid.Children.Add(CreateScaleTick("0", 0.0, true));
            scaleGrid.Children.Add(CreateScaleTick("-5", 0.12));
            scaleGrid.Children.Add(CreateScaleTick("-10", 0.24));
            scaleGrid.Children.Add(CreateScaleTick("-15", 0.36));
            scaleGrid.Children.Add(CreateScaleTick("-20", 0.48));
            scaleGrid.Children.Add(CreateScaleTick("-30", 0.65));
            scaleGrid.Children.Add(CreateScaleTick("-40", 0.82));
            scaleGrid.Children.Add(CreateScaleTick("-50", 1.0));
            Grid.SetColumn(scaleGrid, 1);
            faderAreaGrid.Children.Add(scaleGrid);

            // Twin Stereo Meter Columns (L & R)
            var metersContainer = new Grid
            {
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(1, 14, 2, 6)
            };
            metersContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Meters
            metersContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) }); // "L  R" labels

            var barsGrid = new Grid();
            barsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Meter L
            var meterBgL = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0),
                ClipToBounds = true
            };
            var meterGridL = new Grid();
                        ctrl.MeterFillL = new Border
            {
                Background = CreateMeterGradient(),
                OpacityMask = CreateMeterMask(),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 0
            };
            ctrl.MeterPeakTickL = new Border
            {
                Background = Brushes.White,
                Height = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed
            };
            meterGridL.Children.Add(ctrl.MeterFillL);
            meterGridL.Children.Add(ctrl.MeterPeakTickL);
            meterBgL.Child = meterGridL;
            Grid.SetColumn(meterBgL, 0);
            barsGrid.Children.Add(meterBgL);

            // Meter R
            var meterBgR = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 0, 1, 0),
                ClipToBounds = true
            };
            var meterGridR = new Grid();
                        ctrl.MeterFillR = new Border
            {
                Background = CreateMeterGradient(),
                OpacityMask = CreateMeterMask(),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 0
            };
            ctrl.MeterPeakTickR = new Border
            {
                Background = Brushes.White,
                Height = 2,
                VerticalAlignment = VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed
            };
            meterGridR.Children.Add(ctrl.MeterFillR);
            meterGridR.Children.Add(ctrl.MeterPeakTickR);
            meterBgR.Child = meterGridR;
            Grid.SetColumn(meterBgR, 1);
            barsGrid.Children.Add(meterBgR);

            Grid.SetRow(barsGrid, 0);
            metersContainer.Children.Add(barsGrid);

            // "L R" subtle bottom label
            var lrLabels = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            lrLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lrLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var txtL = new TextBlock { Text = "L", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)), FontSize = 7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var txtR = new TextBlock { Text = "R", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)), FontSize = 7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(txtL, 0);
            Grid.SetColumn(txtR, 1);
            lrLabels.Children.Add(txtL);
            lrLabels.Children.Add(txtR);
            Grid.SetRow(lrLabels, 1);
            metersContainer.Children.Add(lrLabels);

            Grid.SetColumn(metersContainer, 2);
            faderAreaGrid.Children.Add(metersContainer);
            stripStack.Children.Add(faderAreaGrid);

            // 8. Fader Value Digital Readout Box (+0,00)
            ctrl.TxtFaderDb = new TextBlock
            {
                Text = settings.FaderLevel <= -59 ? "-INF" : $"{settings.FaderLevel:+0.00;-0.00;0.00}",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var faderBox = new Border
            {
                Height = 20,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x38)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4, 0, 4, 4),
                Child = ctrl.TxtFaderDb
            };
            stripStack.Children.Add(faderBox);

            // 9. Rotary Pan Knob Section
            var panContainer = new Border
            {
                Height = 66,
                Background = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x16)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(2, 2, 2, 2)
            };
            var panKnob = CreateRotaryKnob(
                minLabel: "L",
                maxLabel: "R",
                initialValue: settings.Pan,
                min: -100.0,
                max: 100.0,
                unit: "",
                arcColor: Color.FromRgb(0x4C, 0xAF, 0x50),
                showDelay: false,
                onValueChanged: val =>
                {
                    settings.Pan = val;
                    config.Save();
                }
            );
            panContainer.Child = panKnob;
            stripStack.Children.Add(panContainer);

            // 10. Mode Pill Buttons (ON, AFV, OFF)
                                    var btnRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ctrl.BtnAfv = new Button
            {
                Content = "AFV",
                Height = 22,
                Margin = new Thickness(0, 0, 1, 0),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = "interactive"
            };
            ctrl.BtnOn = new Button
            {
                Content = "ON",
                Height = 22,
                Margin = new Thickness(0, 0, 1, 0),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = "interactive"
            };

            ctrl.BtnOff = new Button
            {
                Content = "OFF",
                Height = 22,
                Margin = new Thickness(0, 0, 0, 0),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = "interactive"
            };
            Grid.SetColumn(ctrl.BtnAfv, 0);
            Grid.SetColumn(ctrl.BtnOn, 1);
            Grid.SetColumn(ctrl.BtnOff, 2);
            btnRow.Children.Add(ctrl.BtnAfv);
            btnRow.Children.Add(ctrl.BtnOn);
            btnRow.Children.Add(ctrl.BtnOff);
            
            UpdatePillButtonVisual(ctrl.BtnOn, settings.IsOn && !settings.IsAfv, Color.FromRgb(0xFF, 0x98, 0x00));
            UpdatePillButtonVisual(ctrl.BtnAfv, settings.IsAfv && !settings.IsOn, Color.FromRgb(0xFF, 0x98, 0x00));
            UpdatePillButtonVisual(ctrl.BtnOff, !settings.IsOn && !settings.IsAfv, Color.FromRgb(0xFF, 0x98, 0x00));

            ctrl.BtnOn.Click += async (s, e) =>
            {
                if (_switcher != null)
                {
                    var target = (settings.IsOn && !settings.IsAfv) ? AudioMixOption.Off : AudioMixOption.On;
                    await _audioService.SetMixOptionAsync(inputId, target);
                }
            };
            ctrl.BtnAfv.Click += async (s, e) =>
            {
                if (_switcher != null)
                {
                    var target = (settings.IsAfv && !settings.IsOn) ? AudioMixOption.Off : AudioMixOption.Afv;
                    await _audioService.SetMixOptionAsync(inputId, target);
                }
            };
            ctrl.BtnOff.Click += async (s, e) =>
            {
                if (_switcher != null)
                {
                    await _audioService.SetMixOptionAsync(inputId, AudioMixOption.Off);
                }
            };
            stripStack.Children.Add(btnRow);

            ctrl.BtnSolo = new Button
            {
                Content = "H/P",
                Height = 22,
                FontSize = 10,
                Background = settings.Solo ? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
                Foreground = settings.Solo ? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)) : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x88)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Solo / Monitor Channel",
                Tag = "interactive"
            };
            ctrl.BtnSolo.Click += (s, e) =>
            {
                settings.Solo = !settings.Solo;
                ctrl.BtnSolo.Background = settings.Solo ? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A));
                ctrl.BtnSolo.Foreground = settings.Solo ? new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)) : new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x88));
                config.Save();
            };
            stripStack.Children.Add(ctrl.BtnSolo);

            stripBorder.Child = stripStack;
            _stripControllers.Add(ctrl);
            return stripBorder;
        }

        #endregion

        #region Master Strip Builder

        private UIElement CreateMasterStrip()
        {
            var config = MainWindow.Instance?.GetConfig() ?? InputConfig.Load();
            var settings = config.GetChannelAudio((int)LogicalAudioChannels.Master);

            var ctrl = new ChannelStripController
            {
                InputId = (int)LogicalAudioChannels.Master,
                IsMaster = true,
                Settings = settings
            };

            var stripBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E)),
                Width = 110,
                Padding = new Thickness(5, 4, 5, 4)
            };

            var stripStack = new StackPanel();

            // 1. Red Master Tally
            ctrl.TallyBar = new Border
            {
                Height = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                Margin = new Thickness(4, 0, 4, 3)
            };
            stripStack.Children.Add(ctrl.TallyBar);

            // 2. Title
            ctrl.TitleText = new TextBlock
            {
                Text = "Master",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stripStack.Children.Add(ctrl.TitleText);

            // 3. Input row spacer (matches 82px)
            stripStack.Children.Add(new Border { Height = 82, Margin = new Thickness(0, 0, 0, 4) });

            // 4. Master Equalizer ("Wave Adjuster")
            var eqContainer = new Button { Height = 44, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x13)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, ToolTip = "Click to open Master Equalizer", Tag = "interactive", Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Style = (Style)this.FindResource("InvisibleButtonStyle") ?? new Style() };
            ctrl.EqCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = "interactive"
            };
            eqContainer.Content = ctrl.EqCanvas;
            ctrl.RedrawEq = () => DrawMiniEqCurve(ctrl.EqCanvas, settings.Eq);
            ctrl.EqCanvas.Loaded += (s, e) => ctrl.RedrawEq();

            eqContainer.Click += (s, e) => { OpenEqWindow(LogicalAudioChannels.Master, "Master Out"); };
            stripStack.Children.Add(eqContainer);

            // 5. Master Dynamics ("Wave Adjuster")
            var dynContainer = new Button { Height = 44, Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x0F, 0x13)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 4), Cursor = Cursors.Hand, ToolTip = "Click to open Master Dynamics (Compressor/Limiter)", Tag = "interactive", Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Style = (Style)this.FindResource("InvisibleButtonStyle") ?? new Style() };
            ctrl.DynCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = "interactive"
            };
            dynContainer.Content = ctrl.DynCanvas;
            ctrl.RedrawDyn = () => DrawMiniDynCurve(ctrl.DynCanvas, settings.Dynamics);
            ctrl.DynCanvas.Loaded += (s, e) => ctrl.RedrawDyn();

            dynContainer.Click += (s, e) => { OpenDynWindow(LogicalAudioChannels.Master, "Master Out"); };
            stripStack.Children.Add(dynContainer);

            // 6. Master dB Peak Numeric Readout
            ctrl.TxtPeakDb = new TextBlock
            {
                Text = "0.00",
                Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)),
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            stripStack.Children.Add(ctrl.TxtPeakDb);

            // 7. Master Fader Track, Scale & Twin Stereo Meters
            var faderAreaGrid = new Grid
            {
                Height = 188,
                Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            faderAreaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            faderAreaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            faderAreaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

            ctrl.FaderSlider = new Slider
            {
                Style = (Style)FindResource("FairlightFaderStyle"),
                Orientation = Orientation.Vertical,
                Minimum = -60,
                Maximum = 10,
                Value = settings.FaderLevel,
                Height = 180,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Tag = "interactive"
            };
            ctrl.FaderSlider.ValueChanged += async (s, e) =>
            {
                settings.FaderLevel = e.NewValue;
                ctrl.TxtFaderDb.Text = e.NewValue <= -59 ? "-INF" : $"{e.NewValue:+0.00;-0.00;0.00}";
                try { await _audioService.SetVolumeAsync(LogicalAudioChannels.Master, e.NewValue); } catch { }
            };
            Grid.SetColumn(ctrl.FaderSlider, 0);
            faderAreaGrid.Children.Add(ctrl.FaderSlider);

            // Scale
            var scaleGrid = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(0, 16, 2, 16) };
            scaleGrid.Children.Add(CreateScaleTick("0", 0.0, true));
            scaleGrid.Children.Add(CreateScaleTick("-5", 0.12));
            scaleGrid.Children.Add(CreateScaleTick("-10", 0.24));
            scaleGrid.Children.Add(CreateScaleTick("-15", 0.36));
            scaleGrid.Children.Add(CreateScaleTick("-20", 0.48));
            scaleGrid.Children.Add(CreateScaleTick("-30", 0.65));
            scaleGrid.Children.Add(CreateScaleTick("-40", 0.82));
            scaleGrid.Children.Add(CreateScaleTick("-50", 1.0));
            Grid.SetColumn(scaleGrid, 1);
            faderAreaGrid.Children.Add(scaleGrid);

            // Master Meters
            var metersContainer = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Margin = new Thickness(1, 14, 2, 6) };
            metersContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            metersContainer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });

            var barsGrid = new Grid();
            barsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var meterBgL = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)), CornerRadius = new CornerRadius(1), Margin = new Thickness(1, 0, 1, 0), ClipToBounds = true };
            var meterGridL = new Grid();
                        ctrl.MeterFillL = new Border { Background = CreateMeterGradient(), OpacityMask = CreateMeterMask(), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            ctrl.MeterPeakTickL = new Border { Background = Brushes.White, Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
            meterGridL.Children.Add(ctrl.MeterFillL);
            meterGridL.Children.Add(ctrl.MeterPeakTickL);
            meterBgL.Child = meterGridL;
            Grid.SetColumn(meterBgL, 0);
            barsGrid.Children.Add(meterBgL);

            var meterBgR = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)), CornerRadius = new CornerRadius(1), Margin = new Thickness(1, 0, 1, 0), ClipToBounds = true };
            var meterGridR = new Grid();
                        ctrl.MeterFillR = new Border { Background = CreateMeterGradient(), OpacityMask = CreateMeterMask(), VerticalAlignment = VerticalAlignment.Bottom, Height = 0 };
            ctrl.MeterPeakTickR = new Border { Background = Brushes.White, Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
            meterGridR.Children.Add(ctrl.MeterFillR);
            meterGridR.Children.Add(ctrl.MeterPeakTickR);
            meterBgR.Child = meterGridR;
            Grid.SetColumn(meterBgR, 1);
            barsGrid.Children.Add(meterBgR);

            Grid.SetRow(barsGrid, 0);
            metersContainer.Children.Add(barsGrid);

            var lrLabels = new Grid { Margin = new Thickness(0, 1, 0, 0) };
            lrLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lrLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var txtL = new TextBlock { Text = "L", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)), FontSize = 7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            var txtR = new TextBlock { Text = "R", Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)), FontSize = 7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(txtL, 0);
            Grid.SetColumn(txtR, 1);
            lrLabels.Children.Add(txtL);
            lrLabels.Children.Add(txtR);
            Grid.SetRow(lrLabels, 1);
            metersContainer.Children.Add(lrLabels);

            Grid.SetColumn(metersContainer, 2);
            faderAreaGrid.Children.Add(metersContainer);
            stripStack.Children.Add(faderAreaGrid);

            // 8. Master Fader Readout Box (+2,00)
            ctrl.TxtFaderDb = new TextBlock
            {
                Text = settings.FaderLevel <= -59 ? "-INF" : $"{settings.FaderLevel:+0.00;-0.00;0.00}",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var faderBox = new Border
            {
                Height = 20,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x38)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(4, 0, 4, 4),
                Child = ctrl.TxtFaderDb
            };
            stripStack.Children.Add(faderBox);

            // 9. Disabled AFV pill button
            var btnAfvDisabled = new Button
            {
                Content = "AFV",
                Height = 20,
                FontSize = 9,
                IsEnabled = false,
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x26)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x50)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x32)),
                Margin = new Thickness(4, 0, 4, 6)
            };
            stripStack.Children.Add(btnAfvDisabled);

            // 10. Headphones / Kopfhörer Section with Rotary Knob & Mini Sliders
            var hpContainer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x13, 0x13, 0x17)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 4, 3, 4),
                Tag = "interactive"
            };
            var hpStack = new StackPanel();

            var hpTitle = new TextBlock
            {
                Text = "Headphones",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99)),
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            };
            hpStack.Children.Add(hpTitle);

            // Headphone Volume Rotary Knob (MIN .. MAX)
            var hpKnob = CreateRotaryKnob(
                minLabel: "MIN",
                maxLabel: "MAX",
                initialValue: config.HeadphonesLevel,
                min: -60.0,
                max: 10.0,
                unit: "",
                arcColor: Color.FromRgb(0x4C, 0xAF, 0x50),
                showDelay: false,
                onValueChanged: val =>
                {
                    config.HeadphonesLevel = val;
                    config.Save();
                }
            );
            hpStack.Children.Add(hpKnob);

            // 3 Mini Sliders (Speaker, Headphone, Mic)
            var miniSlidersGrid = new Grid
            {
                Height = 42,
                Margin = new Thickness(6, 4, 6, 2),
                Tag = "interactive"
            };
            miniSlidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            miniSlidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            miniSlidersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var sldSpk = new Slider { Style = (Style)FindResource("MiniMonitorSliderStyle"), Minimum = 0, Maximum = 100, Value = 80, Tag = "interactive" };
            var sldHp = new Slider { Style = (Style)FindResource("MiniMonitorSliderStyle"), Minimum = 0, Maximum = 100, Value = 75, Tag = "interactive" };
            var sldMic = new Slider { Style = (Style)FindResource("MiniMonitorSliderStyle"), Minimum = 0, Maximum = 100, Value = 65, Tag = "interactive" };
            Grid.SetColumn(sldSpk, 0);
            Grid.SetColumn(sldHp, 1);
            Grid.SetColumn(sldMic, 2);
            miniSlidersGrid.Children.Add(sldSpk);
            miniSlidersGrid.Children.Add(sldHp);
            miniSlidersGrid.Children.Add(sldMic);
            hpStack.Children.Add(miniSlidersGrid);

            // 3 Mute Buttons (Speaker, Headphone, Mic)
            var hpBtnGrid = new Grid { Tag = "interactive" };
            hpBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hpBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hpBtnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var btnSpeaker = new Button
            {
                Content = "MON",
                Height = 20,
                FontSize = 9,
                Background = config.MonitorMuted ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Mute Studio Monitor",
                Tag = "interactive"
            };
            btnSpeaker.Click += (s, e) =>
            {
                config.MonitorMuted = !config.MonitorMuted;
                btnSpeaker.Background = config.MonitorMuted ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A));
                config.Save();
            };

            var btnHp = new Button
            {
                Content = "H/P",
                Height = 20,
                FontSize = 9,
                Margin = new Thickness(1, 0, 1, 0),
                Background = config.HeadphonesMuted ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Mute Headphones",
                Tag = "interactive"
            };
            btnHp.Click += (s, e) =>
            {
                config.HeadphonesMuted = !config.HeadphonesMuted;
                btnHp.Background = config.HeadphonesMuted ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A));
                config.Save();
            };

            var btnMic = new Button
            {
                Content = "MIC",
                Height = 20,
                FontSize = 9,
                Background = config.TalkbackMuted ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Mute Talkback",
                Tag = "interactive"
            };
            btnMic.Click += (s, e) =>
            {
                config.TalkbackMuted = !config.TalkbackMuted;
                btnMic.Background = config.TalkbackMuted ? new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)) : new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2A));
                config.Save();
            };

            Grid.SetColumn(btnSpeaker, 0);
            Grid.SetColumn(btnHp, 1);
            Grid.SetColumn(btnMic, 2);
            hpBtnGrid.Children.Add(btnSpeaker);
            hpBtnGrid.Children.Add(btnHp);
            hpBtnGrid.Children.Add(btnMic);
            hpStack.Children.Add(hpBtnGrid);

            hpContainer.Child = hpStack;
            stripStack.Children.Add(hpContainer);

            stripBorder.Child = stripStack;
            _stripControllers.Add(ctrl);
            return stripBorder;
        }

        #endregion

        #region Dialog Openers for Wave Adjusters (EQ & Dynamics)

        private void OpenEqWindow(long inputId, string channelTitle)
        {
            try
            {
                var win = new FairlightEqWindow(_audioService, inputId, channelTitle);
                win.Owner = Application.Current.MainWindow;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open EQ: {ex.Message}\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDynWindow(long inputId, string channelTitle)
        {
            try
            {
                var win = new FairlightDynamicsWindow(_audioService, inputId, channelTitle);
                win.Owner = Application.Current.MainWindow;
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open Dynamics: {ex.Message}\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Rotary Knob Helper (Exact Blackmagic Studio Styling & Flawless Drag)

        private UIElement CreateRotaryKnob(
            string minLabel,
            string maxLabel,
            double initialValue,
            double min,
            double max,
            string unit,
            Color arcColor,
            bool showDelay,
            Action<double> onValueChanged)
        {
            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "interactive"
            };

            // Knob Graphic Grid (36x36)
            var knobGrid = new Grid
            {
                Width = 36,
                Height = 36,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 1, 0, 2),
                Tag = "interactive"
            };

            // Outer dark bezel with ticks
            var outerBezel = new Ellipse
            {
                Width = 34,
                Height = 34,
                Fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1E)),
                Stroke = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x38)),
                StrokeThickness = 1.0,
                Tag = "interactive"
            };
            knobGrid.Children.Add(outerBezel);

            // Arc indicator path
            var arcPath = new Path
            {
                Stroke = new SolidColorBrush(arcColor),
                StrokeThickness = 2.0,
                Tag = "interactive"
            };
            knobGrid.Children.Add(arcPath);

            // Metallic Silver Center Disc
            var centerDisc = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.4),
                    GradientOrigin = new Point(0.5, 0.3),
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0xEE, 0xEE, 0xF4), 0.0),
                        new GradientStop(Color.FromRgb(0xD0, 0xD0, 0xD8), 0.55),
                        new GradientStop(Color.FromRgb(0x98, 0x98, 0xA2), 1.0)
                    }
                },
                Stroke = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x54)),
                StrokeThickness = 1.0,
                Tag = "interactive"
            };
            knobGrid.Children.Add(centerDisc);

            // Position Indicator Line
            var pointerLine = new Line
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22)),
                StrokeThickness = 2.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Tag = "interactive"
            };
            knobGrid.Children.Add(pointerLine);

            // Min/Max Labels row around knob
            var labelGrid = new Grid { Width = 36, Margin = new Thickness(0, -6, 0, 0), Tag = "interactive" };
            labelGrid.Children.Add(new TextBlock { Text = minLabel, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x76)), FontSize = 7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Left, Tag = "interactive" });
            labelGrid.Children.Add(new TextBlock { Text = maxLabel, Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x76)), FontSize = 7, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right, Tag = "interactive" });

            // Value Box below knob
            var valText = new TextBlock
            {
                Text = FormatKnobValue(initialValue, unit),
                Foreground = Brushes.White,
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "interactive"
            };
            var valBox = new Border
            {
                Height = 16,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x1C)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x38)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(2, 2, 2, 0),
                Child = valText,
                Tag = "interactive"
            };

            container.Children.Add(knobGrid);
            container.Children.Add(labelGrid);
            container.Children.Add(valBox);

            if (showDelay)
            {
                var delayText = new TextBlock
                {
                    Text = "--> 0",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                    FontSize = 7,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 0),
                    Tag = "interactive"
                };
                container.Children.Add(delayText);
            }

            double currentVal = initialValue;

            void UpdateVisual(double val)
            {
                double norm = Math.Max(0.0, Math.Min(1.0, (val - min) / (max - min)));
                double angleDeg = -135.0 + norm * 270.0;
                double angleRad = (angleDeg - 90.0) * Math.PI / 180.0;

                double cx = 18.0, cy = 18.0;
                double r = 8.5;

                pointerLine.X1 = cx;
                pointerLine.Y1 = cy;
                pointerLine.X2 = cx + r * Math.Cos(angleRad);
                pointerLine.Y2 = cy + r * Math.Sin(angleRad);

                valText.Text = FormatKnobValue(val, unit);
            }

            UpdateVisual(currentVal);

            // Flawless Mouse Drag Interaction (Immune to ScrollViewer capture)
            Point dragStart = default;
            bool isDragging = false;

            knobGrid.PreviewMouseLeftButtonDown += (s, e) =>
            {
                isDragging = true;
                dragStart = e.GetPosition(this);
                knobGrid.CaptureMouse();
                e.Handled = true;
            };

            knobGrid.PreviewMouseMove += (s, e) =>
            {
                if (isDragging && knobGrid.IsMouseCaptured)
                {
                    var pt = e.GetPosition(this);
                    double dy = dragStart.Y - pt.Y;
                    dragStart = pt;

                    double step = (max - min) / 75.0;
                    currentVal = Math.Max(min, Math.Min(max, currentVal + dy * step));
                    UpdateVisual(currentVal);
                    onValueChanged?.Invoke(currentVal);
                    e.Handled = true;
                }
            };

            knobGrid.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (isDragging)
                {
                    isDragging = false;
                    knobGrid.ReleaseMouseCapture();
                    e.Handled = true;
                }
            };

            knobGrid.PreviewMouseWheel += (s, e) =>
            {
                double step = (max - min) / 35.0;
                currentVal = Math.Max(min, Math.Min(max, currentVal + (e.Delta > 0 ? step : -step)));
                UpdateVisual(currentVal);
                onValueChanged?.Invoke(currentVal);
                e.Handled = true;
            };

            // Double click resets to 0.0
            knobGrid.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    currentVal = 0.0;
                    UpdateVisual(currentVal);
                    onValueChanged?.Invoke(currentVal);
                    e.Handled = true;
                }
            };

            return container;
        }

        private static string FormatKnobValue(double val, string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return $"{val:+0.00;-0.00;0.00}";
            }
            return $"{val:+0.00;-0.00;0.00}";
        }

        #endregion

        #region Scale Ticks Helper

        private static UIElement CreateScaleTick(string text, double normY, bool isBold = false)
        {
            var tickGrid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, normY * 148.0, 0, 0)
            };

            var txt = new TextBlock
            {
                Text = text,
                Foreground = isBold ? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xBA)) : new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x66)),
                FontSize = 7.5,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, -5, 4, 0)
            };
            var tick = new Border
            {
                Width = 3,
                Height = 1,
                Background = isBold ? new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99)) : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x52)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            tickGrid.Children.Add(txt);
            tickGrid.Children.Add(tick);
            return tickGrid;
        }

        #endregion

        #region Mini Curves Rendering (EQ & Dynamics)

        private static void DrawMiniEqCurve(Canvas canvas, EqSettings eq)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 90;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 42;

            // Zero line
            canvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = w,
                Y1 = h / 2.0,
                Y2 = h / 2.0,
                Stroke = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x2E)),
                StrokeThickness = 1
            });

            if (eq == null || !eq.Enabled)
            {
                // Flat cyan line
                canvas.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = w,
                    Y1 = h / 2.0,
                    Y2 = h / 2.0,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    StrokeThickness = 1.4
                });
                return;
            }

            var fig = new PathFigure { StartPoint = new Point(0, h / 2.0) };
            var geo = new PathGeometry();

            int steps = 36;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double freq = 20.0 * Math.Pow(1000.0, t);
                double gain = CalcMiniEqGain(eq, freq);
                double x = t * w;
                double y = h / 2.0 - (gain / 18.0) * (h / 2.0);
                y = Math.Max(2, Math.Min(h - 2, y));

                if (i == 0) fig.StartPoint = new Point(x, y);
                else fig.Segments.Add(new LineSegment(new Point(x, y), true));
            }
            geo.Figures.Add(fig);

            canvas.Children.Add(new Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1.8
            });
        }

        private static double CalcMiniEqGain(EqSettings eq, double f)
        {
            double g = 0;
            g += CalcBandGain(eq.Band1, f);
            g += CalcBandGain(eq.Band2, f);
            g += CalcBandGain(eq.Band3, f);
            g += CalcBandGain(eq.Band4, f);
            g += CalcBandGain(eq.Band5, f);
            g += CalcBandGain(eq.Band6, f);
            return g;
        }

        private static double CalcBandGain(EqBandSettings b, double f)
        {
            if (b == null || !b.Enabled || Math.Abs(b.Gain) < 0.01) return 0;
            double f0 = b.Frequency;
            double q = Math.Max(0.2, b.Q);
            double ratio = f / f0;
            return b.Gain / (1.0 + Math.Pow(q * (ratio - 1.0 / ratio), 2.0));
        }

        private static void DrawMiniDynCurve(Canvas canvas, DynamicsSettings dyn)
        {
            if (canvas == null) return;
            canvas.Children.Clear();

            double w = canvas.ActualWidth > 0 ? canvas.ActualWidth : 90;
            double h = canvas.ActualHeight > 0 ? canvas.ActualHeight : 42;

            // 1:1 Unity dashed line
            canvas.Children.Add(new Line
            {
                X1 = 0,
                Y1 = h,
                X2 = w,
                Y2 = 0,
                Stroke = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x30)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 2 }
            });

            if (dyn == null || !dyn.Enabled)
            {
                // Simple unity line
                canvas.Children.Add(new Line
                {
                    X1 = 0,
                    Y1 = h,
                    X2 = w,
                    Y2 = 0,
                    Stroke = new SolidColorBrush(Color.FromRgb(0x55, 0x66, 0x55)),
                    StrokeThickness = 1.5
                });
                return;
            }

            var fig = new PathFigure { StartPoint = new Point(0, h) };
            var geo = new PathGeometry();

            int steps = 28;
            for (int i = 0; i <= steps; i++)
            {
                double inDb = -60.0 + (i / (double)steps) * 60.0;
                double outDb = inDb;

                if (dyn.GateEnabled && inDb < dyn.GateThreshold)
                {
                    double diff = dyn.GateThreshold - inDb;
                    outDb = dyn.GateThreshold - (diff * dyn.GateRatio);
                    double floor = dyn.GateThreshold + dyn.GateRange;
                    if (outDb < floor) outDb = floor;
                }

                if (dyn.CompEnabled)
                {
                    if (inDb > dyn.CompThreshold)
                        outDb = dyn.CompThreshold + (inDb - dyn.CompThreshold) / dyn.CompRatio;
                    outDb += dyn.CompGain;
                }

                if (dyn.LimiterEnabled && outDb > dyn.LimiterThreshold)
                    outDb = dyn.LimiterThreshold;

                outDb = Math.Max(-60.0, Math.Min(0.0, outDb));

                double x = ((inDb + 60.0) / 60.0) * w;
                double y = h - ((outDb + 60.0) / 60.0) * h;

                if (i == 0) fig.StartPoint = new Point(x, y);
                else fig.Segments.Add(new LineSegment(new Point(x, y), true));
            }
            geo.Figures.Add(fig);

            canvas.Children.Add(new Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1.8
            });
        }

        #endregion

        #region Helpers & Visuals

                private static LinearGradientBrush CreateMeterGradient()
        {
            return new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x00, 0xC8, 0x53), 0.0),  // Green
                    new GradientStop(Color.FromRgb(0x00, 0xC8, 0x53), 0.68), // Green up to -18dB
                    new GradientStop(Color.FromRgb(0xFF, 0xEB, 0x3B), 0.72), // Yellow
                    new GradientStop(Color.FromRgb(0xFF, 0xEB, 0x3B), 0.90), // Yellow up to -2dB
                    new GradientStop(Color.FromRgb(0xF4, 0x43, 0x36), 0.93), // Red above -2dB
                    new GradientStop(Color.FromRgb(0xF4, 0x43, 0x36), 1.0)
                }
            };
        }

        private static Brush CreateMeterMask()
        {
            var drawingGroup = new DrawingGroup();
            drawingGroup.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 10, 3))));
            return new DrawingBrush(drawingGroup)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 10, 4),
                ViewportUnits = BrushMappingMode.Absolute
            };
        }

        private static void UpdatePillButtonVisual(Button btn, bool active, Color activeColor)
        {
            if (active)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(0x28, 0x22, 0x18));
                btn.BorderBrush = new SolidColorBrush(activeColor);
                btn.Foreground = new SolidColorBrush(activeColor);
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x28));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x36));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x82));
            }
        }

        #endregion

        #region 30 FPS Live Meter Tick

                        private void OnAudioServiceMeterChanged(object? sender, AudioMeterState state) { _liveAudioLevels[state.ChannelId] = (state.LevelDb, state.PeakDb); }

                private void OnAudioStateChanged(AudioState state)
        {
            Dispatcher.InvokeAsync(() => {
                var strip = _stripControllers.FirstOrDefault(s => s.InputId == state.InputId);
                if (strip != null)
                {
                    strip.Settings.IsOn = state.IsOn;
                    strip.Settings.IsAfv = state.IsAfv;
                    UpdatePillButtonVisual(strip.BtnOn, state.IsOn, Color.FromRgb(0xFF, 0x98, 0x00));
                    UpdatePillButtonVisual(strip.BtnAfv, state.IsAfv, Color.FromRgb(0xFF, 0x98, 0x00));
                    UpdatePillButtonVisual(strip.BtnOff, !state.IsOn && !state.IsAfv, Color.FromRgb(0xFF, 0x98, 0x00));
                }
            }); 
        } 
        
        private void MeterTimer_Tick(object? sender, EventArgs e)
        {
            double maxMeterH = 158.0;

            foreach (var strip in _stripControllers)
            {
                bool isActive = strip.IsMaster || strip.Settings.IsOn || strip.Settings.IsAfv;

                strip.MeterFillL.Background = isActive ? _meterActiveBrush : _meterInactiveBrush;
                strip.MeterFillR.Background = isActive ? _meterActiveBrush : _meterInactiveBrush;

                double targetL = 0.0;
                double targetR = 0.0;

                                    if (_liveAudioLevels.TryGetValue(strip.InputId, out var levels))
                    {
                        double dbToNorm(double db) => db <= -60 ? 0.0 : db >= 6 ? 1.0 : (db + 60.0) / 66.0;

                        double normL = dbToNorm(levels.L);
                        double normR = dbToNorm(levels.R);

                        // Professional mixers show pre-fader levels on the meters
                        targetL = Math.Min(1.0, normL);
                        targetR = Math.Min(1.0, normR);
                    }
                    else
                    {
                        targetL = 0.0;
                                                targetR = 0.0;
                    }

                    // Smooth attack and decay
                if (targetL > strip.CurrentLevelL) strip.CurrentLevelL = targetL;
                else strip.CurrentLevelL = Math.Max(0.0, strip.CurrentLevelL * 0.85);

                if (targetR > strip.CurrentLevelR) strip.CurrentLevelR = targetR;
                else strip.CurrentLevelR = Math.Max(0.0, strip.CurrentLevelR * 0.85);

                // Peak Hold
                if (strip.CurrentLevelL >= strip.PeakLevelL)
                {
                    strip.PeakLevelL = strip.CurrentLevelL;
                    strip.PeakHoldTicksL = 40;
                }
                else
                {
                    if (strip.PeakHoldTicksL > 0) strip.PeakHoldTicksL--;
                    else strip.PeakLevelL = Math.Max(0.0, strip.PeakLevelL * 0.92);
                }

                if (strip.CurrentLevelR >= strip.PeakLevelR)
                {
                    strip.PeakLevelR = strip.CurrentLevelR;
                    strip.PeakHoldTicksR = 40;
                }
                else
                {
                    if (strip.PeakHoldTicksR > 0) strip.PeakHoldTicksR--;
                    else strip.PeakLevelR = Math.Max(0.0, strip.PeakLevelR * 0.92);
                }

                // Update Meter Height
                strip.MeterFillL.Height = strip.CurrentLevelL * maxMeterH;
                strip.MeterFillR.Height = strip.CurrentLevelR * maxMeterH;

                // Update Peak Hold Ticks
                if (strip.PeakLevelL > 0.05)
                {
                    strip.MeterPeakTickL.Visibility = Visibility.Visible;
                    strip.MeterPeakTickL.Margin = new Thickness(0, 0, 0, Math.Max(0, strip.PeakLevelL * maxMeterH - 2));
                }
                else
                {
                    strip.MeterPeakTickL.Visibility = Visibility.Collapsed;
                }

                if (strip.PeakLevelR > 0.05)
                {
                    strip.MeterPeakTickR.Visibility = Visibility.Visible;
                    strip.MeterPeakTickR.Margin = new Thickness(0, 0, 0, Math.Max(0, strip.PeakLevelR * maxMeterH - 2));
                }
                else
                {
                    strip.MeterPeakTickR.Visibility = Visibility.Collapsed;
                }

                // Update Numerical Peak dB
                double peakDb = -60.0 + Math.Max(strip.PeakLevelL, strip.PeakLevelR) * 70.0;
                if (peakDb <= -58.0)
                {
                    strip.TxtPeakDb.Text = "-INF";
                    strip.TxtPeakDb.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x74));
                }
                else
                {
                    strip.TxtPeakDb.Text = $"{peakDb:+0.00;-0.00;0.00}";
                    if (peakDb >= 0.0)
                        strip.TxtPeakDb.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)); // Red
                    else if (peakDb >= -18.0)
                        strip.TxtPeakDb.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0x3B)); // Yellow
                    else
                        strip.TxtPeakDb.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); // Green
                }

                // Update Tally Bar
                if (strip.IsMaster)
                {
                    strip.TallyBar.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                }
                else if (strip.Settings.IsOn)
                {
                    strip.TallyBar.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
                }
                else if (strip.Settings.IsAfv)
                {
                    strip.TallyBar.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
                }
                else
                {
                    strip.TallyBar.Background = new SolidColorBrush(Color.FromRgb(0x38, 0x38, 0x44));
                }
            }
        }

        #endregion

        #region Safe Drag & Wheel Horizontal Scroll Handling (Zero Interception of Child Clicks)

        private bool _isPotentialDrag = false;
        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _dragStartOffsetX;
        private double _dragStartOffsetY;

        private static bool IsInteractiveControl(DependencyObject? dep)
        {
            while (dep != null)
            {
                if (dep is System.Windows.Controls.Button || dep is System.Windows.Controls.ComboBox || dep is System.Windows.Controls.Slider ||
                    dep is System.Windows.Controls.Primitives.Thumb ||
                    dep is System.Windows.Controls.Primitives.ToggleButton ||
                    dep is System.Windows.Controls.TextBox ||
                    (dep is FrameworkElement fe && (fe.Tag as string) == "interactive"))
                    return true;
                dep = VisualTreeHelper.GetParent(dep);
            }
            return false;
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && e.Delta != 0)
            {
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                {
                    if (MainScrollViewer != null) 
                        MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
                }
                else
                {
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                }
                e.Handled = true;
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInteractiveControl(e.OriginalSource as DependencyObject))
            {
                _isPotentialDrag = false;
                return; // Let child element handle the click directly!
            }

            if (sender is ScrollViewer scrollViewer)
            {
                _isPotentialDrag = true;
                _isDragging = false;
                _dragStartPoint = e.GetPosition(this);
                _dragStartOffsetX = scrollViewer.HorizontalOffset;
                if (MainScrollViewer != null)
                {
                    _dragStartOffsetY = MainScrollViewer.VerticalOffset;
                }
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPotentialDrag && sender is ScrollViewer scrollViewer)
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                {
                    if (scrollViewer.IsMouseCaptured)
                    {
                        scrollViewer.ReleaseMouseCapture();
                    }
                    _isPotentialDrag = false;
                    _isDragging = false;
                    return;
                }

                var currentPoint = e.GetPosition(this);
                Vector delta = currentPoint - _dragStartPoint;

                // Engage mouse capture when user moves horizontally OR vertically past 5px threshold!
                if (!_isDragging && (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5))
                {
                    _isDragging = true;
                    scrollViewer.CaptureMouse();
                }

                if (_isDragging && scrollViewer.IsMouseCaptured)
                {
                    scrollViewer.ScrollToHorizontalOffset(_dragStartOffsetX - delta.X);
                    if (MainScrollViewer != null)
                    {
                        MainScrollViewer.ScrollToVerticalOffset(_dragStartOffsetY - delta.Y);
                    }
                }
            }
        }

        private void ScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && sender is ScrollViewer scrollViewer && scrollViewer.IsMouseCaptured)
            {
                scrollViewer.ReleaseMouseCapture();
                _isDragging = false;
                _isPotentialDrag = false;
                e.Handled = true; // Prevent click from firing after a pan
            }
            else
            {
                _isPotentialDrag = false;
            }
        }

        #endregion
    }






























}
