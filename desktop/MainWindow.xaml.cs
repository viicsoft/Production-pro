using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Core;
using Simulator;
using BMDSwitcherAPI;

namespace Desktop
{
    public partial class MainWindow : Window
    {
        // ---- simulator + tally logic ----
        private readonly IAtemSwitch _switcher = new SimAtem();
        private readonly ObservableCollection<TallyUpdate> _tallies = new();
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;

        private const int TallyServerPort = 5160;

        // ---- real ATEM hardware fields ----
        private IBMDSwitcher? _atem;
        private IBMDSwitcherMixEffectBlock? _meBlock;
        // maps logical button index (1..N) to ATEM input ID
        private readonly Dictionary<int, long> _inputIds = new();
        
        // Button references for dynamic styling
        private readonly Dictionary<int, System.Windows.Controls.Button> _programButtons = new();
        private readonly Dictionary<int, System.Windows.Controls.Button> _previewButtons = new();
        
        // Fader state tracking
        private bool _isTransitioning = false;
        private double _lastFaderValue = 0;
        
        // ATEM hardware state polling (for bidirectional sync)
        private System.Threading.Timer? _atemPollTimer;
        private long _lastProgramInput = -1;
        private long _lastPreviewInput = -1;
        
        // Mixer selection (determines functional button count)
        private int _selectedInputCount = 4; // Default to Mini Pro ISO
        
        // Debug logging to file
        private static readonly string LogFile = @"C:\worker\AtemDirector\desktop\debug.log";
        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(LogFile, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            }
            catch { }
        }

        public MainWindow()
        {
            InitializeComponent();
            TallyList.ItemsSource = _tallies;
            
            // Initialize button mappings for dynamic styling (using 1-based input IDs)
            _programButtons[1] = PgmBtn1;
            _programButtons[2] = PgmBtn2;
            _programButtons[3] = PgmBtn3;
            _programButtons[4] = PgmBtn4;
            _programButtons[5] = PgmBtn5;
            _programButtons[6] = PgmBtn6;
            _programButtons[7] = PgmBtn7;
            _programButtons[8] = PgmBtn8;
            _programButtons[9] = PgmBtn9;
            _programButtons[10] = PgmBtn10;
            _programButtons[11] = PgmBtn11;
            _programButtons[12] = PgmBtn12;
            _programButtons[13] = PgmBtn13;
            _programButtons[14] = PgmBtn14;
            _programButtons[15] = PgmBtn15;
            _programButtons[16] = PgmBtn16;
            _programButtons[17] = PgmBtn17;
            _programButtons[18] = PgmBtn18;
            _programButtons[19] = PgmBtn19;
            _programButtons[20] = PgmBtn20;
            _programButtons[21] = PgmBtn21;
            _programButtons[22] = PgmBtn22;
            _programButtons[23] = PgmBtn23;
            _programButtons[24] = PgmBtn24;
            _programButtons[25] = PgmBtn25;
            _programButtons[26] = PgmBtn26;
            _programButtons[27] = PgmBtn27;
            _programButtons[28] = PgmBtn28;
            _programButtons[29] = PgmBtn29;
            _programButtons[30] = PgmBtn30;
            _programButtons[31] = PgmBtn31;
            _programButtons[32] = PgmBtn32;
            _programButtons[33] = PgmBtn33;
            _programButtons[34] = PgmBtn34;
            _programButtons[35] = PgmBtn35;
            _programButtons[36] = PgmBtn36;
            _programButtons[37] = PgmBtn37;
            _programButtons[38] = PgmBtn38;
            _programButtons[39] = PgmBtn39;
            _programButtons[40] = PgmBtn40;

            _previewButtons[1] = PrvBtn1;
            _previewButtons[2] = PrvBtn2;
            _previewButtons[3] = PrvBtn3;
            _previewButtons[4] = PrvBtn4;
            _previewButtons[5] = PrvBtn5;
            _previewButtons[6] = PrvBtn6;
            _previewButtons[7] = PrvBtn7;
            _previewButtons[8] = PrvBtn8;
            _previewButtons[9] = PrvBtn9;
            _previewButtons[10] = PrvBtn10;
            _previewButtons[11] = PrvBtn11;
            _previewButtons[12] = PrvBtn12;
            _previewButtons[13] = PrvBtn13;
            _previewButtons[14] = PrvBtn14;
            _previewButtons[15] = PrvBtn15;
            _previewButtons[16] = PrvBtn16;
            _previewButtons[17] = PrvBtn17;
            _previewButtons[18] = PrvBtn18;
            _previewButtons[19] = PrvBtn19;
            _previewButtons[20] = PrvBtn20;
            _previewButtons[21] = PrvBtn21;
            _previewButtons[22] = PrvBtn22;
            _previewButtons[23] = PrvBtn23;
            _previewButtons[24] = PrvBtn24;
            _previewButtons[25] = PrvBtn25;
            _previewButtons[26] = PrvBtn26;
            _previewButtons[27] = PrvBtn27;
            _previewButtons[28] = PrvBtn28;
            _previewButtons[29] = PrvBtn29;
            _previewButtons[30] = PrvBtn30;
            _previewButtons[31] = PrvBtn31;
            _previewButtons[32] = PrvBtn32;
            _previewButtons[33] = PrvBtn33;
            _previewButtons[34] = PrvBtn34;
            _previewButtons[35] = PrvBtn35;
            _previewButtons[36] = PrvBtn36;
            _previewButtons[37] = PrvBtn37;
            _previewButtons[38] = PrvBtn38;
            _previewButtons[39] = PrvBtn39;
            _previewButtons[40] = PrvBtn40;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1) simulator (for tallies + operator app)
                await _switcher.ConnectAsync(Host.Text);
                MessageBox.Show("Connected to Simulator", "AtemDirector");

                // 2) hardware ATEM over USB
                ConnectToHardwareAtem();

                // 3) tally websocket server (CLIENT connection)
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                var uri = new Uri($"ws://127.0.0.1:{TallyServerPort}/ws/tally");
                await _ws.ConnectAsync(uri, CancellationToken.None);

                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _ = PumpTalliesAndBroadcast(_cts.Token);

                StatusText.Text = "Connected";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection failed: {ex.Message}");
            }
        }
        
        private async void MixerSelection_Click(object sender, RoutedEventArgs e)
        {
            // Determine selected input count
            if (RadioMiniProISO.IsChecked == true)
            {
                _selectedInputCount = 4;
                Log("[MIXER] Selected: ATEM Mini Pro ISO (4 inputs)");
            }
            else if (RadioMiniExtreme.IsChecked == true)
            {
                _selectedInputCount = 8;
                Log("[MIXER] Selected: ATEM Mini Extreme (8 inputs)");
            }
            else if (RadioConstellation.IsChecked == true)
            {
                _selectedInputCount = 40;
                Log("[MIXER] Selected: ATEM 4 M/E Constellation HD (40 inputs)");
            }
            
            // Hide the overlay
            MixerSelectionOverlay.Visibility = Visibility.Collapsed;
            
            // Trigger connection (same as clicking Connect button)
            Connect_Click(sender, e);
        }

        // ============ HARDWARE CONNECT ============

        private void ConnectToHardwareAtem()
        {
            Log("[DEBUG] ConnectToHardwareAtem called");
            try
            {
                Log("[DEBUG] Attempting to discover ATEM...");
                var discovery = new CBMDSwitcherDiscovery();
                _BMDSwitcherConnectToFailure failReason;

                discovery.ConnectTo(string.Empty, out IBMDSwitcher switcher, out failReason);
                if (switcher == null)
                    throw new Exception($"ConnectTo returned null. Reason: {failReason}");

                _atem = switcher;

                // --- get ME1 block ---
                Guid meIterGuid = typeof(IBMDSwitcherMixEffectBlockIterator).GUID;
                IntPtr meIterPtr;
                _atem.CreateIterator(meIterGuid, out meIterPtr);
                var meIter = (IBMDSwitcherMixEffectBlockIterator)
                    Marshal.GetObjectForIUnknown(meIterPtr);

                meIter.Next(out _meBlock);
                if (_meBlock == null)
                    throw new Exception("Could not acquire MixEffect block (ME1) from ATEM.");

                // --- build input map (1..N) ---
                BuildInputMap();
                
                // --- Monitor for changes (Bidirectional Sync) ---
                // CRITICAL: Must be a class member to avoid Garbage Collection!
                _monitor = new MixEffectBlockMonitor(_meBlock);
                _monitor.ProgramInputChanged += (physicalId) =>
                {
                    // Reverse lookup: Physical ID -> Logical Index (1..40)
                    long logicalId = GetLogicalInputId(physicalId);
                    
                    // DEBUG LOG
                    Log($"[DEBUG] PGM Change: Phys={physicalId} Log={logicalId}");
                    
                    if (logicalId > 0)
                    {
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _switcher.CutAsync(0, (int)logicalId);
                        });
                    }
                };
                _monitor.PreviewInputChanged += (physicalId) =>
                {
                    // Reverse lookup
                    long logicalId = GetLogicalInputId(physicalId);
                    
                    // DEBUG LOG
                    Log($"[DEBUG] PVW Change: Phys={physicalId} Log={logicalId}");

                    if (logicalId > 0)
                    {
                        Dispatcher.InvokeAsync(async () =>
                        {
                            await _switcher.SetPreviewAsync(0, (int)logicalId);
                        });
                    }
                };
                
                _meBlock.AddCallback(_monitor);
                Log("[DEBUG] Monitor callback registered successfully");
                
                // Start polling ATEM state (alternative to callbacks which don't work reliably)
                Log("[DEBUG] About to call StartAtemPolling...");
                StartAtemPolling();
                Log("[DEBUG] StartAtemPolling returned");

                _atem.GetProductName(out string productName);
                MessageBox.Show($"Connected to physical ATEM: {productName}", "AtemDirector");
            }
            catch (Exception ex)
            {
                // don’t kill the app – simulator still works fine
                MessageBox.Show(
                    "Could not connect to physical ATEM over USB.\n" +
                    "You can still use the simulator.\n\n" +
                    $"Details: {ex.Message}",
                    "AtemDirector",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        
        // Keep reference alive
        private MixEffectBlockMonitor? _monitor;
        
        private void StartAtemPolling()
        {
            // Stop any existing timer
            _atemPollTimer?.Dispose();
            
            // Initialize state
            if (_meBlock != null)
            {
                try
                {
                    _meBlock.GetProgramInput(out _lastProgramInput);
                    _meBlock.GetPreviewInput(out _lastPreviewInput);
                    Log($"[POLL] Initial state - PGM:{_lastProgramInput} PVW:{_lastPreviewInput}");
                }
                catch (Exception ex)
                {
                    Log($"[POLL] Failed to get initial state: {ex.Message}");
                }
            }
            
            // Start polling every 100ms
            _atemPollTimer = new System.Threading.Timer(PollAtemState, null, 100, 100);
            Log("[POLL] Started polling ATEM state every 100ms");
        }
        
        private void StopAtemPolling()
        {
            _atemPollTimer?.Dispose();
            _atemPollTimer = null;
            _lastProgramInput = -1;
            _lastPreviewInput = -1;
            Log("[POLL] Stopped polling");
        }
        
        private void PollAtemState(object? state)
        {
            if (_meBlock == null) return;
            
            try
            {
                // COM objects must be accessed on the thread that created them (UI thread)
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Get current hardware state
                        _meBlock.GetProgramInput(out long currentPgm);
                        _meBlock.GetPreviewInput(out long currentPvw);
                        
                        // Detect Program change (skip first poll)
                        if (currentPgm != _lastProgramInput && _lastProgramInput != -1)
                        {
                            Log($"[POLL] Program changed: {_lastProgramInput} -> {currentPgm}");
                            long logicalId = GetLogicalInputId(currentPgm);
                            Log($"[POLL] PGM Change: Phys={currentPgm} Log={logicalId}");
                            
                            if (logicalId > 0)
                            {
                                Dispatcher.InvokeAsync(async () =>
                                {
                                    await _switcher.CutAsync(0, (int)logicalId);
                                });
                            }
                        }
                        
                        // Detect Preview change (skip first poll)
                        if (currentPvw != _lastPreviewInput && _lastPreviewInput != -1)
                        {
                            Log($"[POLL] Preview changed: {_lastPreviewInput} -> {currentPvw}");
                            long logicalId = GetLogicalInputId(currentPvw);
                            Log($"[POLL] PVW Change: Phys={currentPvw} Log={logicalId}");
                            
                            if (logicalId > 0)
                            {
                                Dispatcher.InvokeAsync(async () =>
                                {
                                    await _switcher.SetPreviewAsync(0, (int)logicalId);
                                });
                            }
                        }
                        
                        _lastProgramInput = currentPgm;
                        _lastPreviewInput = currentPvw;
                        
                        // Update UI button colors immediately
                        long currentPgmLogical = GetLogicalInputId(currentPgm);
                        long currentPvwLogical = GetLogicalInputId(currentPvw);
                        if (currentPgmLogical > 0 || currentPvwLogical > 0)
                        {
                            UpdateButtonStyles((int)currentPgmLogical, (int)currentPvwLogical);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[POLL] Error accessing ATEM: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"[POLL] Dispatcher error: {ex.Message}");
            }
        }
        
        private long GetLogicalInputId(long physicalId)
        {
            // Reverse lookup: Find KEY (Logical 1..40) where VALUE == physicalId.
            // Since we enforce strict 1:1 mapping for 1..40 in BuildInputMap, 
            // the logical ID is often the same, but let's be safe.
            foreach (var kvp in _inputIds)
            {
                if (kvp.Value == physicalId) return kvp.Key;
            }
            return -1;
        }

        // ============ MONITOR CALLBACK CLASS ============
        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        public class MixEffectBlockMonitor : IBMDSwitcherMixEffectBlockCallback
        {
            private readonly IBMDSwitcherMixEffectBlock _meBlock;
            public event Action<long>? ProgramInputChanged;
            public event Action<long>? PreviewInputChanged;

            public MixEffectBlockMonitor(IBMDSwitcherMixEffectBlock meBlock)
            {
                _meBlock = meBlock;
            }

            // 4CC Codes from BMD SDK: 
            // ProgramInput = 'pgin' = 0x7067696E = 1885825390
            // PreviewInput = 'pvin' = 0x7076696E = 1886873966
            private const long bmdSwitcherMixEffectBlockPropertyIdProgramInput = 1885825390;
            private const long bmdSwitcherMixEffectBlockPropertyIdPreviewInput = 1886873966;

            public void PropertyChanged(long propertyId)
            {
                Log($"[DEBUG] PropertyChanged called: ID={propertyId} (0x{propertyId:X})");
                
                if (propertyId == bmdSwitcherMixEffectBlockPropertyIdProgramInput)
                {
                    _meBlock.GetProgramInput(out long pgmId);
                    Log($"[DEBUG] Program Input changed to: {pgmId}");
                    ProgramInputChanged?.Invoke(pgmId);
                }
                else if (propertyId == bmdSwitcherMixEffectBlockPropertyIdPreviewInput) 
                {
                    _meBlock.GetPreviewInput(out long pvwId);
                    System.Diagnostics.Debug.WriteLine($"[CALLBACK] Preview Input changed to: {pvwId}");
                    PreviewInputChanged?.Invoke(pvwId);
                }
                else
                {
                    Log($"[DEBUG] Unknown property ID: {propertyId} (0x{propertyId:X})");
                }
            }

            public void Notify(_BMDSwitcherMixEffectBlockEventType eventType)
            {
                // Required by interface but we only care about PropertyChanged for inputs
            }
        }

        private void BuildInputMap()
        {
            _inputIds.Clear();
            if (_atem == null) return;

            Guid inputIterGuid = typeof(IBMDSwitcherInputIterator).GUID;
            IntPtr inputIterPtr;
            _atem.CreateIterator(inputIterGuid, out inputIterPtr);
            var inputIter = (IBMDSwitcherInputIterator)Marshal.GetObjectForIUnknown(inputIterPtr);

            while (true)
            {
                IBMDSwitcherInput input;
                inputIter.Next(out input);
                if (input == null) break;

                input.GetInputId(out long id);
                
                // PATCH: Strictly map Input IDs 1-40 to their respective buttons.
                // This prevents "Black" (ID 0) from shifting everything by one.
                if (id >= 1 && id <= 40)
                {
                    _inputIds[(int)id] = id;
                }
                
                // Note: If we need Black/Bars mapped to specific buttons later, 
                // we can add explicit checks here (e.g. if id == 0 => map to button X).
            }
        }

        // ============ TALLY PUMP (CLIENT MODE) ============

        private async Task PumpTalliesAndBroadcast(CancellationToken token)
        {
            await foreach (var state in _switcher.StateStream(token))
            {
                try {
                // Compute Tally Updates
                var updates = TallyEngine.Compute(state)
                                         .Where(t => t.MeIndex == 0)
                                         .OrderBy(t => t.CamAlias)
                                         .ToList();

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _tallies.Clear();
                    foreach (var t in updates) _tallies.Add(t);
                    
                    // Update button styles based on current state
                    var me = state.MEs.FirstOrDefault(m => m.MeIndex == 0);
                    if (me != null)
                    {
                        var programInput = (int)(me.Program.FirstOrDefault());
                        var previewInput = (int)(me.Preview.FirstOrDefault());
                        UpdateButtonStyles(programInput, previewInput);
                    }
                });

                // Broadcast to websocket server (Relay)
                if (_ws is { State: WebSocketState.Open })
                {
                    foreach (var t in updates)
                    {
                        var payload = JsonSerializer.Serialize(new
                        {
                            type = "update",
                            alias = t.CamAlias,
                            PGM = t.Program,
                            PVW = t.Preview
                        });
                        var bytes = Encoding.UTF8.GetBytes(payload);
                        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, token);
                    }
                }
                } catch {}
            }
        }

        // ============ BUTTON STYLE UPDATES ============
        
        private void UpdateButtonStyles(int programInput, int previewInput)
        {
            // Reset all buttons to default style
            foreach (var btn in _programButtons.Values)
                btn.Style = (Style)FindResource("SwitcherButton");
            foreach (var btn in _previewButtons.Values)
                btn.Style = (Style)FindResource("SwitcherButton");
            
            // Highlight active program button (red)
            if (_programButtons.TryGetValue(programInput, out var pgmBtn))
                pgmBtn.Style = (Style)FindResource("ProgramButton");
            
            // Highlight active preview button (green)
            if (_previewButtons.TryGetValue(previewInput, out var prvBtn))
                prvBtn.Style = (Style)FindResource("PreviewButton");
        }

        // ============ PROGRAM CUTS ============

        private async Task PerformCutAsync(int inputIndex)
        {
            // Check if button is within functional range
            if (inputIndex > _selectedInputCount)
            {
                Log($"[INPUT] Button {inputIndex} disabled (max: {_selectedInputCount})");
                return; // Ignore clicks on disabled buttons
            }
            
            // 1) simulator (keeps tallies + operator app in sync)
            await _switcher.CutAsync(0, inputIndex);

            // 2) mirror to physical ATEM if connected
            try
            {
                if (_meBlock != null && _inputIds.TryGetValue(inputIndex, out long inputId))
                {
                    _meBlock.SetProgramInput(inputId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hardware program cut failed: {ex.Message}", "AtemDirector",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============ PREVIEW SELECT ============

        private async Task PerformPreviewSelect(int inputIndex)
        {
            // Check if button is within functional range
            if (inputIndex > _selectedInputCount)
            {
                Log($"[INPUT] Button {inputIndex} disabled (max: {_selectedInputCount})");
                return; // Ignore clicks on disabled buttons
            }
            
            // 1) Update simulator (keeps tallies + UI in sync)
            await _switcher.SetPreviewAsync(0, inputIndex);

            // 2) Mirror to physical ATEM if connected
            try
            {
                if (_meBlock != null && _inputIds.TryGetValue(inputIndex, out long inputId))
                {
                    _meBlock.SetPreviewInput(inputId);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hardware preview select failed: {ex.Message}", "AtemDirector",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============ TRANSITION CONTROLS ============

        private void CutTransition_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _meBlock?.PerformCut();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PerformCut failed: {ex.Message}", "AtemDirector",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoTransition_Click(object sender, RoutedEventArgs e)
        {
            // Simple auto transition trigger
            try
            {
                _meBlock?.PerformAutoTransition();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PerformAutoTransition failed: {ex.Message}", "AtemDirector",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PalettesDropdown_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SidebarContent == null) return;

            // Index 0: Palettes (Visible), Others: Hidden (for now, or logic as needed)
            // User wants toggleable. Let's assume selecting "Palettes" opens it.
            // We should ideally have a "Close" option or similar. 
            // For now, let's say "Palettes", "Media", "Output" show content, but we might add a "Close" item.
            // Wait, I haven't added "Close" item yet. I will rely on the XAML update for that.
            
            var combo = sender as System.Windows.Controls.ComboBox;
            if (combo?.SelectedIndex == 0) // "Close Panel" (I will add this as first item)
            {
                SidebarContent.Visibility = Visibility.Collapsed;
            }
            else
            {
                SidebarContent.Visibility = Visibility.Visible;
            }
        }

        // ============ FADER HANDLERS ============

        private async void TransitionFader_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isTransitioning) return;

            var val = e.NewValue; // 0..100
            
            // Only trigger action if we moved significantly or reached the end?
            // "Functional" usually means as you drag, it sets the transition position.
            // But ATEM transition position is a property `SetTransitionPosition`.
            // The simulator currently just has Cut/Auto.
            // For now, let's just trigger a CUT when we reach the top, then reset.
            // This mimics a "drag to cut" roughly, without the intermediate mix effect visible (unless we add that capability).
            
            if (val > 99 && _lastFaderValue < 99)
            {
                // Completed the move
                await CompleteTransition();
            }
            // If implementing T-Bar properly, we'd call _switcher.SetTransitionPosition(val/100).
            
            _lastFaderValue = val;
        }

        private void TransitionFader_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var delta = e.Delta > 0 ? 5 : -5; // 5% increment per scroll
            var newValue = Math.Clamp(TransitionFader.Value + delta, 0, 100);
            TransitionFader.Value = newValue;
            e.Handled = true;
        }

        private async Task CompleteTransition()
        {
            _isTransitioning = true;

            try
            {
                // Get current preview input
                var state = await _switcher.GetStateAsync();
                var me = state.MEs.FirstOrDefault(m => m.MeIndex == 0);
                if (me != null)
                {
                    var previewInput = (int)me.Preview.FirstOrDefault();

                    // Cut preview to program
                    await PerformCutAsync(previewInput);
                }

                // Reset fader to bottom
                await Task.Delay(100); // Small delay for visual feedback
                TransitionFader.Value = 0;
                _lastFaderValue = 0;
            }
            finally
            {
                _isTransitioning = false;
            }
        }

        // ============ BUTTON HANDLERS (PROGRAM) ============

        private async void Cut1_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(1); // CAM1
        private async void Cut2_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(2); // CAM2
        private async void Cut3_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(3); // CAM3
        private async void Cut4_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(4); // CAM4
        private async void Cut5_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(5); // BLK
        private async void Cut6_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(6); // COL1
        private async void Cut7_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(7); // COL2
        private async void Cut8_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(8); // BARS
        private async void Cut9_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(9); // MP1
        private async void Cut10_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(10); // MP2 (New)
        private async void Cut11_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(11);
        private async void Cut12_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(12);
        private async void Cut13_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(13);
        private async void Cut14_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(14);
        private async void Cut15_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(15);
        private async void Cut16_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(16);
        private async void Cut17_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(17);
        private async void Cut18_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(18);
        private async void Cut19_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(19);
        private async void Cut20_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(20);
        private async void Cut21_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(21);
        private async void Cut22_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(22);
        private async void Cut23_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(23);
        private async void Cut24_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(24);
        private async void Cut25_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(25);
        private async void Cut26_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(26);
        private async void Cut27_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(27);
        private async void Cut28_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(28);
        private async void Cut29_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(29);
        private async void Cut30_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(30);
        private async void Cut31_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(31);
        private async void Cut32_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(32);
        private async void Cut33_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(33);
        private async void Cut34_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(34);
        private async void Cut35_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(35);
        private async void Cut36_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(36);
        private async void Cut37_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(37);
        private async void Cut38_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(38);
        private async void Cut39_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(39);
        private async void Cut40_Click(object sender, RoutedEventArgs e) => await PerformCutAsync(40);

        // ============ BUTTON HANDLERS (PREVIEW) ============

        private async void Prev1_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(1); // CAM1
        private async void Prev2_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(2); // CAM2
        private async void Prev3_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(3); // CAM3
        private async void Prev4_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(4); // CAM4
        private async void Prev5_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(5); // BLK
        private async void Prev6_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(6); // COL1
        private async void Prev7_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(7); // COL2
        private async void Prev8_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(8); // BARS
        private async void Prev9_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(9); // MP1
        private async void Prev10_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(10); // MP2 (New)
        private async void Prev11_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(11);
        private async void Prev12_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(12);
        private async void Prev13_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(13);
        private async void Prev14_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(14);
        private async void Prev15_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(15);
        private async void Prev16_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(16);
        private async void Prev17_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(17);
        private async void Prev18_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(18);
        private async void Prev19_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(19);
        private async void Prev20_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(20);
        private async void Prev21_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(21);
        private async void Prev22_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(22);
        private async void Prev23_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(23);
        private async void Prev24_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(24);
        private async void Prev25_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(25);
        private async void Prev26_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(26);
        private async void Prev27_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(27);
        private async void Prev28_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(28);
        private async void Prev29_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(29);
        private async void Prev30_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(30);
        private async void Prev31_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(31);
        private async void Prev32_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(32);
        private async void Prev33_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(33);
        private async void Prev34_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(34);
        private async void Prev35_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(35);
        private async void Prev36_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(36);
        private async void Prev37_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(37);
        private async void Prev38_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(38);
        private async void Prev39_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(39);
        private async void Prev40_Click(object sender, RoutedEventArgs e) => await PerformPreviewSelect(40);
    }
}
