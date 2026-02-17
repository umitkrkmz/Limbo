using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Limbo.Core.Analysis;
using Limbo.Core.Bypass;
using Limbo.Core.Config;
using Limbo.Core.Detection;
using Limbo.Core.Engine;
using Limbo.Core.Monitoring;
using Limbo.Core.Profiles;
using Limbo.Driver;
using Microsoft.Win32;

namespace Limbo.GUI;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ProfileViewModel> _profiles = [];
    private readonly LimboConfig _config;
    private readonly ProfileManager _profileManager;
    private readonly DispatcherTimer _statsTimer;
    private readonly BandwidthMonitor _bandwidthMonitor = new();
    private readonly BlacklistManager _blacklistManager = new();
    private readonly IspBehaviorAnalyzer _analyzer = new();
    private readonly ConnectionTracker _tracker = new();

    private WinDivertDriver? _driver;
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private bool _isRunning;
    private bool _forceClose;
    private TaskbarIcon? _trayIcon;

    // Statistics
    private long _packetsProcessed;
    private long _bytesProcessed;
    private long _matchedPackets;
    private long _modifiedPackets;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public MainWindow()
    {
        InitializeComponent();

        // Load config
        _config = ConfigManager.Load();
        _profileManager = new ProfileManager();

        // --- RADAR SYSTEM WIRING (GUI) ---
    
        // 1. Connect Analyzer to Storage
        _analyzer.OnBlockDetected += domain => 
        {
            _blacklistManager.AddToBlacklist(domain);
        };

        // 2. NEW: ConnectionTracker (Sessiz Drop) -> Blacklist
        _tracker.OnSilentDropDetected += domain => 
        {
            // Olayın UI Thread'inde işlenmesi için Dispatcher kullanıyoruz (Arayüz kitlenmesin diye)
            Dispatcher.Invoke(() => 
            {
                AddLog($"[RADAR] Timeout / Silent Drop Detected: {domain}", false, true);
                _blacklistManager.AddToBlacklist(domain);
            });
        };

        // 3. Connect Storage to GUI Profiles
        _blacklistManager.OnNewBlacklistedDomain += domain =>
        {
            // UI thread must be used to update ObservableCollection and Logs
            Dispatcher.Invoke(() => 
            {
                var newProfile = new DomainProfile 
                { 
                    Domain = domain, 
                    Method = BypassMethod.Adaptive,
                    IsVerified = false
                };
                
                _profileManager.SaveProfile(newProfile);
                LoadProfiles(); // Refresh the list in the UI
                AddLog($"[RADAR] Auto-Blacklisted: {domain}", false, true);
            });
        };

        // 4. Load existing Auto-Blacklist.txt on startup
        var blacklisted = _blacklistManager.GetAllBlacklisted();
        foreach (var domain in blacklisted)
        {
            if (_profileManager.GetProfile(domain).Method == BypassMethod.None) 
            {
                _profileManager.SaveProfile(new DomainProfile { Domain = domain, Method = BypassMethod.Adaptive });
            }
        }
        // ---------------------------------


        // Load profiles
        foreach (var profileConfig in _config.Profiles)
        {
            _profileManager.SaveProfile(ProfileFromConfig(profileConfig));
        }

        ProfilesList.ItemsSource = _profiles;
        LoadProfiles();

        // TEST: Let's trigger it directly in the GUI!
        _blacklistManager.AddToBlacklist("gui-radar-test.com");

        // Setup settings
        DoHToggle.IsChecked = _config.Dns.UseDoH;
        DoHServerBox.Text = _config.Dns.DoHServer;

        // Application settings
        CloseToTrayToggle.IsChecked = _config.CloseToTray;
        MinimizeToTrayToggle.IsChecked = _config.MinimizeToTray;
        StartMinimizedToggle.IsChecked = _config.StartMinimized;

        // Theme setting
        var themeMode = ThemeManager.ParseTheme(_config.Theme);
        ThemeCombo.SelectedIndex = themeMode switch
        {
            LimboThemeMode.Light => 1,
            LimboThemeMode.Dark => 2,
            _ => 0
        };

        // Stats timer
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statsTimer.Tick += StatsTimer_Tick;

        // Check admin
        if (!IsAdministrator())
        {
            AddLog("[!] Not running as Administrator - engine will not work", true);
            EngineToggle.IsEnabled = false;
            StatusText.Text = "NO ADMIN";
            StatusText.Foreground = FindResource("ErrorBrush") as Brush;
            StatusSubtext.Text = "Restart as Administrator";
        }

        // Initialize system tray
        Loaded += (s, e) =>
        {
            InitializeTrayIcon();
            if (_config.StartMinimized)
            {
                MinimizeToTray();
            }
        };
    }


    /// <summary>
    /// Extracts IPv4 or IPv6 address directly from the raw packet span
    /// </summary>
    private string GetIpAddress(ReadOnlySpan<byte> packet, bool isIPv6, bool getSource)
    {
        try
        {
            if (!isIPv6)
            {
                // IPv4: Source IP starts at byte 12, Destination at byte 16 (4 bytes each)
                var offset = getSource ? 12 : 16;
                return new System.Net.IPAddress(packet.Slice(offset, 4)).ToString();
            }
            else
            {
                // IPv6: Source IP starts at byte 8, Destination at byte 24 (16 bytes each)
                var offset = getSource ? 8 : 24;
                return new System.Net.IPAddress(packet.Slice(offset, 16)).ToString();
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private void LoadProfiles()
    {
        _profiles.Clear();
        foreach (var profile in _profileManager.GetAllProfiles())
        {
            _profiles.Add(new ProfileViewModel
            {
                Domain = profile.Domain,
                Method = profile.Method.ToString(),
                FragmentSize = $"Frag: {profile.FragmentSize}"
            });
        }
    }

    private void StatsTimer_Tick(object? sender, EventArgs e)
    {
        PacketsText.Text = FormatNumber(Interlocked.Read(ref _packetsProcessed));
        MatchedText.Text = Interlocked.Read(ref _matchedPackets).ToString("N0");
        ModifiedText.Text = Interlocked.Read(ref _modifiedPackets).ToString("N0");
        DataText.Text = FormatBytes(Interlocked.Read(ref _bytesProcessed));

        // Update bandwidth stats
        var stats = _bandwidthMonitor.GetStats();
        DownloadRateText.Text = BandwidthStats.FormatRate(stats.DownloadRate);
        UploadRateText.Text = BandwidthStats.FormatRate(stats.UploadRate);
        PeakDownloadText.Text = BandwidthStats.FormatRate(stats.PeakDownload);
        PeakUploadText.Text = BandwidthStats.FormatRate(stats.PeakUpload);
    }

    private async void EngineToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        _isRunning = true;

        StatusText.Text = "STARTING...";
        StatusText.Foreground = FindResource("WarningBrush") as Brush;
        StatusSubtext.Text = "Initializing WinDivert";

        AddLog("[*] Starting Limbo engine...");

        try
        {
            _driver = new WinDivertDriver();
            
            // Filter: outbound HTTPS/HTTP traffic
            var filter = "outbound and (tcp.DstPort == 443 or tcp.DstPort == 80 or " +
                         "(udp.DstPort >= 50000 and udp.DstPort <= 65535))";
            
            AddLog($"[*] Filter: {filter}");
            
            if (!_driver.Open(filter))
            {
                throw new Exception("Failed to open WinDivert handle - run as Administrator!");
            }

            AddLog("[+] WinDivert handle opened", false, true); // Green

            _cts = new CancellationTokenSource();
            _captureTask = Task.Run(() => CaptureLoop(_cts.Token));

            StatusText.Text = "ACTIVE";
            StatusText.Foreground = FindResource("SuccessBrush") as Brush;
            StatusSubtext.Text = "Monitoring traffic";

            _statsTimer.Start();
            AddLog("[+] Engine started - waiting for traffic...", false, true);
            UpdateTrayMenu(true);
        }
        catch (Exception ex)
        {
            AddLog($"[X] Error: {ex.Message}", true);
            StatusText.Text = "ERROR";
            StatusText.Foreground = FindResource("ErrorBrush") as Brush;
            StatusSubtext.Text = ex.Message;

            EngineToggle.IsChecked = false;
            _isRunning = false;
        }
    }

    private async void EngineToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        if (!_isRunning) return;

        StatusText.Text = "STOPPING...";
        StatusSubtext.Text = "Please wait";

        AddLog("[*] Stopping engine...");

        _statsTimer.Stop();
        _cts?.Cancel();

        // IMPORTANT: We must close the driver BEFORE waiting for the Task so that Receive() can be unblocked!
        _driver?.Dispose();
        _driver = null;

        try
        {
            if (_captureTask != null)
            {
                // Give it a moment to stop gracefully, but don't hang UI for 5s if it's stuck
                await _captureTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
        catch { }

        // _driver?.Dispose();
        // _driver = null;
        _cts?.Dispose();
        _cts = null;

        StatusText.Text = "INACTIVE";
        StatusText.Foreground = FindResource("TextSecondaryBrush") as Brush;
        StatusSubtext.Text = "Click toggle to start";

        _isRunning = false;
        AddLog("[*] Engine stopped");
        UpdateTrayMenu(false);
    }

    private void CaptureLoop(CancellationToken ct)
    {
        var buffer = new byte[65535];
        var bypassEngine = new BypassEngine();

        try
        {
            while (!ct.IsCancellationRequested && _driver != null)
            {
                if (_driver.Receive(buffer, out var length, out var isOutbound))
                {
                    Interlocked.Increment(ref _packetsProcessed);
                    Interlocked.Add(ref _bytesProcessed, length);

                    _bandwidthMonitor.RecordBytes(length, !isOutbound);

                    var packet = buffer.AsSpan(0, length);
                    // We moved this UP so we can know if the inbound packet is IPv6 and its Port
                    var context = PacketInspector.Analyze(packet, isOutbound);

                    if (!isOutbound)
                    {
                        // --- CONNECTION TRACKER: INBOUND ---
                        // A response arrived! This means the connection was NOT blackholed (dropped)
                        var srcIp = GetIpAddress(packet, context.IsIPv6, getSource: true);
                        _tracker.RegisterInbound(srcIp, context.SrcPort);
                        // -----------------------------------

                        _driver.Send(packet, false);
                        continue;
                    }

                    // --- CONNECTION TRACKER: OUTBOUND ---
                    // Start tracking when we send an initial request to a domain
                    if ((context.IsClientHello || context.HasHttpHost) && !string.IsNullOrEmpty(context.TargetDomain))
                    {
                        var dstIp = GetIpAddress(packet, context.IsIPv6, getSource: false);
                        _tracker.TrackOutbound(dstIp, context.DstPort, context.TargetDomain);
                    }

                    if (context.IsClientHello && context.TargetDomain != null)
                    {
                        var profile = _profileManager.GetProfile(context.TargetDomain);

                        if (profile.Method != BypassMethod.None)
                        {
                            Interlocked.Increment(ref _matchedPackets);
                            
                            Dispatcher.Invoke(() => 
                                AddLog($"[!] Matched: {context.TargetDomain} → {profile.Method}"));

                            var packetContext = new PacketContext
                            {
                                IsOutbound = true,
                                IsTcp = true,
                                IsUdp = false,
                                IsIPv6 = context.IsIPv6,
                                SrcPort = context.SrcPort,
                                DstPort = context.DstPort,
                                IsSyn = context.IsSyn,
                                IsClientHello = true,
                                HasHttpHost = false,
                                TargetDomain = context.TargetDomain
                            };

                            var results = bypassEngine.ProcessPacket(packet, packetContext, profile);

                            if (results.Count > 1)
                            {
                                Interlocked.Increment(ref _modifiedPackets);
                                
                                Dispatcher.Invoke(() => 
                                    AddLog($"[+] Bypassed into {results.Count} packets", false, true));

                                foreach (var result in results)
                                {
                                    _driver.Send(result.Data, result.RecalcChecksum);
                                }
                                continue;
                            }
                        }
                    }

                    _driver.Send(packet, false);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => AddLog($"[X] Capture error: {ex.Message}", true));
        }
    }

    private static ProfileConfig ConfigFromDialog(AddProfileDialog dialog)
    {
        return new ProfileConfig
        {
            Domain = dialog.Domain,
            Method = dialog.SelectedMethod,
            FragmentSize = dialog.FragmentSize,
            SplitPosition = dialog.SplitPosition,
            SplitOffset = dialog.SplitOffset,
            FakeTtl = dialog.TtlValue,
            EchPublicName = dialog.EchPublicName,
            FakePacketSize = dialog.QuicFakeSize,
            UseAdaptiveParams = dialog.UseAdaptiveParams
        };
    }

    private static DomainProfile ProfileFromConfig(ProfileConfig config)
    {
        return new DomainProfile
        {
            Domain = config.Domain,
            Method = config.Method,
            FragmentSize = config.FragmentSize,
            SplitPosition = config.SplitPosition,
            SplitOffset = config.SplitOffset,
            FakeTtl = config.FakeTtl,
            EchPublicName = config.EchPublicName,
            FakePacketSize = config.FakePacketSize,
            UseAdaptiveParams = config.UseAdaptiveParams
        };
    }

    private void AddLog(string message, bool isError = false, bool isSuccess = false)
    {
        // Limit log entries
        if (LogsBox.Document.Blocks.Count > 1000)
        {
            LogsBox.Document.Blocks.Remove(LogsBox.Document.Blocks.FirstBlock);
        }

        var paragraph = new Paragraph(new Run($"[{DateTime.Now:HH:mm:ss}] {message}"));
        paragraph.Margin = new Thickness(0, 0, 0, 4);

        if (isError || message.Contains("[X]") || message.Contains("[!]"))
        {
            paragraph.Foreground = FindResource("ErrorBrush") as Brush;
        }
        else if (isSuccess || message.Contains("[+]"))
        {
            paragraph.Foreground = FindResource("SuccessBrush") as Brush; // Or PrimaryBrush
        }
        else
        {
            paragraph.Foreground = FindResource("TextSecondaryBrush") as Brush;
        }

        LogsBox.Document.Blocks.Add(paragraph);
        LogsBox.ScrollToEnd();
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ProfilesPanel == null || LogsPanel == null || SettingsPanel == null) return;

        ProfilesPanel.Visibility = ProfilesTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LogsPanel.Visibility = LogsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = SettingsTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }


    private void ImportRadar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Title = "Import Radar Blacklist"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                // Call the Import method we created which overwrites Auto-Blacklist.txt
                _blacklistManager.Import(dialog.FileName);
                
                var loadedDomains = _blacklistManager.GetAllBlacklisted();
                
                // Add imported domains to active profiles automatically
                foreach(var domain in loadedDomains)
                {
                    var profile = new DomainProfile 
                    { 
                        Domain = domain, 
                        Method = BypassMethod.Adaptive 
                    };
                    _profileManager.SaveProfile(profile);
                }
                
                LoadProfiles(); // Refresh the GUI List
                AddLog($"[+] Radar: Imported {loadedDomains.Count} domains from {Path.GetFileName(dialog.FileName)}", false, true);
            }
            catch (Exception ex)
            {
                AddLog($"[X] Radar import failed: {ex.Message}", true);
            }
        }
    }

    private void ExportRadar_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt",
            Title = "Export Radar Blacklist",
            FileName = "Limbo_Auto_Blacklist.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                // Call the Export method we created which copies the live memory to the target path
                _blacklistManager.Export(dialog.FileName);
                
                AddLog($"[+] Radar: Exported blacklisted domains to {Path.GetFileName(dialog.FileName)}", false, true);
            }
            catch (Exception ex)
            {
                AddLog($"[X] Radar export failed: {ex.Message}", true);
            }
        }
    }




    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddProfileDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
        {
            var profileConfig = ConfigFromDialog(dialog);
            _profileManager.SaveProfile(ProfileFromConfig(profileConfig));
            _config.Profiles.Add(profileConfig);
            ConfigManager.Save(_config);

            LoadProfiles();
            AddLog($"[+] Added profile: {profileConfig.Domain} → {profileConfig.Method}", false, true);
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ProfileViewModel profile)
        {
            DeleteProfile(profile);
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is ProfileViewModel profile)
        {
            var existingConfig = _config.Profiles.FirstOrDefault(p => p.Domain == profile.Domain);
            if (existingConfig == null) return;

            var dialog = new AddProfileDialog(existingConfig);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                var idx = _config.Profiles.IndexOf(existingConfig);
                var newConfig = ConfigFromDialog(dialog);
                _config.Profiles[idx] = newConfig;

                _profileManager.DeleteProfile(profile.Domain);
                _profileManager.SaveProfile(ProfileFromConfig(newConfig));

                ConfigManager.Save(_config);
                LoadProfiles();
                AddLog($"[*] Updated profile: {newConfig.Domain} → {newConfig.Method}");
            }
        }
    }

    private void DeleteProfileMenu_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is ProfileViewModel profile)
        {
            DeleteProfile(profile);
        }
    }

    private void DeleteProfile(ProfileViewModel profile)
    {
        var result = System.Windows.MessageBox.Show(
            $"Delete profile '{profile.Domain}'?",
            "Confirm Delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            _profileManager.DeleteProfile(profile.Domain);
            _config.Profiles.RemoveAll(p => p.Domain == profile.Domain);
            ConfigManager.Save(_config);
            LoadProfiles();
            AddLog($"[-] Removed profile: {profile.Domain}");
        }
    }

    private void ImportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title = "Import Profiles"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var imported = JsonSerializer.Deserialize<List<ProfileConfig>>(json, JsonOptions);

                if (imported != null && imported.Count > 0)
                {
                    foreach (var p in imported)
                    {
                        var existing = _config.Profiles.FirstOrDefault(x => x.Domain == p.Domain);
                        if (existing != null) _config.Profiles.Remove(existing);
                        
                        _config.Profiles.Add(p);
                        _profileManager.SaveProfile(ProfileFromConfig(p));
                    }
                    
                    ConfigManager.Save(_config);
                    LoadProfiles();
                    AddLog($"[+] Imported {imported.Count} profile(s) from {System.IO.Path.GetFileName(dialog.FileName)}", false, true);
                }
            }
            catch (Exception ex)
            {
                AddLog($"[X] Import failed: {ex.Message}", true);
            }
        }
    }

    private void ExportProfiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Export Profiles",
            FileName = "limbo_profiles.json"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = JsonSerializer.Serialize(_config.Profiles, JsonOptions);
                File.WriteAllText(dialog.FileName, json);
                AddLog($"[+] Exported {_config.Profiles.Count} profile(s) to {System.IO.Path.GetFileName(dialog.FileName)}", false, true);
            }
            catch (Exception ex)
            {
                AddLog($"[X] Export failed: {ex.Message}", true);
            }
        }
    }

    private void ExportSingleProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfilesList.SelectedItem is ProfileViewModel profile)
        {
            var config = _config.Profiles.FirstOrDefault(p => p.Domain == profile.Domain);
            if (config == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                Title = $"Export {profile.Domain}",
                FileName = $"{profile.Domain.Replace("*", "_")}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var list = new List<ProfileConfig> { config };
                    var json = JsonSerializer.Serialize(list, JsonOptions);
                    File.WriteAllText(dialog.FileName, json);
                    AddLog($"[+] Exported profile: {profile.Domain}", false, true);
                }
                catch (Exception ex)
                {
                    AddLog($"[X] Export failed: {ex.Message}", true);
                }
            }
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        LogsBox.Document.Blocks.Clear();
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem item && item.Tag is string themeTag)
        {
            if (Enum.TryParse<LimboThemeMode>(themeTag, out var mode))
            {
                ThemeManager.ApplyTheme(mode);
                _config.Theme = ThemeManager.ThemeToString(mode);
                ConfigManager.Save(_config);
            }
        }
    }

    private void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            System.Windows.MessageBox.Show("Please stop the engine before running connection test.", "Engine Running", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        AddLog("[*] Testing connection to https://www.google.com...");
        
        Task.Run(async () =>
        {
            try
            {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync("https://www.google.com");
                
                Dispatcher.Invoke(() => 
                    AddLog($"[+] Connection OK: {response.StatusCode}", false, true));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => 
                    AddLog($"[X] Connection failed: {ex.Message}", true));
            }
        });
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            System.Windows.MessageBox.Show("Please stop the engine before calibration.", "Engine Running", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var dialog = new InputDialog("Enter domain to calibrate:", "google.com");
        dialog.Owner = this;

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
        {
            AddLog($"[*] Calibrating {dialog.InputText}...");
            AddLog($"[+] TcpFragmentation recommended for {dialog.InputText}", false, true);
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (DoHToggle.IsChecked.HasValue) 
            _config.Dns.UseDoH = DoHToggle.IsChecked.Value;
        
        _config.Dns.DoHServer = DoHServerBox.Text;
        
        if (CloseToTrayToggle.IsChecked.HasValue)
            _config.CloseToTray = CloseToTrayToggle.IsChecked.Value;
            
        if (MinimizeToTrayToggle.IsChecked.HasValue)
            _config.MinimizeToTray = MinimizeToTrayToggle.IsChecked.Value;
            
        if (StartMinimizedToggle.IsChecked.HasValue)
            _config.StartMinimized = StartMinimizedToggle.IsChecked.Value;

        ConfigManager.Save(_config);
        AddLog("[*] Settings saved");
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeBtn_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        
    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        // Don't toggle exit here, just Close() which triggers OnClosing
        Close();
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static string FormatNumber(long num) => num switch
    {
        >= 1_000_000 => $"{num / 1_000_000.0:F1}M",
        >= 1_000 => $"{num / 1_000.0:F1}K",
        _ => num.ToString()
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B"
    };

    #region Tray Icon

    private void InitializeTrayIcon()
    {
        _trayIcon = (TaskbarIcon)FindResource("LimboTrayIcon");
        _trayIcon.IconSource = new BitmapImage(new Uri("pack://application:,,,/Limbo.GUI;component/Resources/limbo.ico"));
        
        // Re-bind events since we can't do it easily in XAML for resource dictionaries
        _trayIcon.TrayLeftMouseUp += (s, e) => ShowWindow();
        
        // Wire up context menu if not auto-wired
        if (_trayIcon.ContextMenu != null)
        {
            foreach (var item in _trayIcon.ContextMenu.Items)
            {
               if (item is MenuItem menuItem)
               {
                   switch (menuItem.Name)
                   {
                       case "TrayStartStop": menuItem.Click += TrayStartStop_Click; break;
                       case "TraySettings": menuItem.Click += TraySettings_Click; break;
                       case "TrayExit": menuItem.Click += TrayExit_Click; break;
                       case "TrayShow": menuItem.Click += (s, e) => ShowWindow(); break;
                   }
               }
            }
        }
        UpdateTrayMenu(false);
    }

    private void TrayStartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            EngineToggle.IsChecked = false;
        }
        else
        {
            EngineToggle.IsChecked = true;
        }
    }

    private void TraySettings_Click(object sender, RoutedEventArgs e)
    {
        ShowWindow();
        SettingsTab.IsChecked = true;
    }

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _forceClose = true;
        
        // Fast cleanup
        _cts?.Cancel(); 
        _driver?.Dispose();
        _trayIcon?.Dispose(); // Explicitly dispose tray icon to remove it immediately
        
        System.Windows.Application.Current.Shutdown();
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MinimizeToTray()
    {
        Hide();
        _trayIcon?.ShowBalloonTip("Limbo", "Running in background. Double-click to open.", 
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private void UpdateTrayMenu(bool isRunning)
    {
        if (_trayIcon?.ContextMenu == null) return;
        
        foreach (var item in _trayIcon.ContextMenu.Items)
        {
            if (item is MenuItem menuItem && menuItem.Name == "TrayStartStop")
            {
                menuItem.Header = isRunning ? "⏹️ Stop Engine" : "▶️ Start Engine";
            }
        }

        if (_trayIcon != null)
            _trayIcon.ToolTipText = isRunning ? "Limbo - Engine Running" : "Limbo - Engine Stopped";
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _config.MinimizeToTray)
        {
            MinimizeToTray();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_config.CloseToTray && !_forceClose)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
        else
        {
            // Fast shutdown from window close button (if not minimized to tray)
            _statsTimer?.Stop();
            _cts?.Cancel();

            // first driver killed
            _driver?.Dispose();
            
            _trayIcon?.Dispose();
            base.OnClosing(e);
        }
    }

    #endregion
}

public class ProfileViewModel
{
    public string Domain { get; set; } = "";
    public string Method { get; set; } = "";
    public string FragmentSize { get; set; } = "";
}
