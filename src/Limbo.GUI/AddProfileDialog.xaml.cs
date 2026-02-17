using System.Windows;
using System.Windows.Controls;
using Limbo.Core.Bypass;
using Limbo.Core.Config;

namespace Limbo.GUI;

public partial class AddProfileDialog : Window
{
    public string Domain { get; private set; } = "";
    public BypassMethod SelectedMethod { get; private set; } = BypassMethod.Adaptive;
    public bool UseAdaptiveParams { get; private set; } = true;
    public int FragmentSize { get; private set; } = 2;
    public int TtlValue { get; private set; } = 3;
    public int SplitPosition { get; private set; } = 3;
    public int SplitOffset { get; private set; } = 3;
    public string EchPublicName { get; private set; } = "";
    public int QuicFakeSize { get; private set; } = 64;
    public int UdpFakeTtl { get; private set; } = 1;

    private readonly ProfileConfig? _editingProfile;

    private static readonly Dictionary<string, string> MethodDescriptions = new()
    {
        ["Adaptive"] = "Automatically tries all methods and saves the best one",
        ["Ech"] = "Encrypts SNI completely - DPI cannot see target domain at all",
        ["TcpFragmentation"] = "Splits TLS ClientHello into small TCP segments",
        ["SmartFragmentation"] = "SIMD-optimized fragmentation with SNI detection",
        ["Desync"] = "Sends fake packets to confuse DPI state machine",
        ["FakeTtl"] = "Sends decoy packets with low TTL that expire before DPI",
        ["UdpFake"] = "Adds fake UDP packets for QUIC/Voice bypass",
        ["QuicFake"] = "Injects fake UDP datagram before QUIC Initial to desync DPI"
    };

    public AddProfileDialog()
    {
        InitializeComponent();
        UpdateUI();
    }

    /// <summary>
    /// Constructor for edit mode - pre-fills values from existing profile
    /// </summary>
    public AddProfileDialog(ProfileConfig existingProfile) : this()
    {
        _editingProfile = existingProfile;

        // Update title
        Title = "Edit Profile";

        // Pre-fill values
        DomainBox.Text = existingProfile.Domain;
        FragmentSize = existingProfile.FragmentSize;

        // Select the correct method
        foreach (ComboBoxItem item in MethodCombo.Items)
        {
            if (item.Tag?.ToString() == existingProfile.Method.ToString())
            {
                item.IsSelected = true;
                break;
            }
        }

        // Update UI
        UpdateUI();
    }

    private void MethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUI();
    }

    private void ManualParamsToggle_Changed(object sender, RoutedEventArgs e)
    {
        UpdateManualParamsVisibility();
    }

    private void UpdateUI()
    {
        if (MethodDescription == null || MethodCombo?.SelectedItem == null) return;
        
        var tag = GetSelectedMethodTag();
        
        // Update description
        if (MethodDescriptions.TryGetValue(tag, out var desc))
        {
            MethodDescription.Text = desc;
        }

        // Show/hide parameter mode toggle (not for Adaptive)
        var showParamToggle = tag != "Adaptive";
        ParamModePanel.Visibility = showParamToggle ? Visibility.Visible : Visibility.Collapsed;

        // Reset toggle when method changes
        if (ManualParamsToggle != null)
        {
            ManualParamsToggle.IsChecked = false;
        }

        UpdateManualParamsVisibility();
    }

    private void UpdateManualParamsVisibility()
    {
        if (ManualParamsPanel == null) return;

        var tag = GetSelectedMethodTag();
        var isManual = ManualParamsToggle?.IsChecked == true;

        // Update description
        if (ParamModeDescription != null)
        {
            ParamModeDescription.Text = isManual 
                ? "You control the exact parameter values" 
                : "Parameters will be auto-tuned for best results";
        }

        // Show manual params panel only if manual mode is on
        ManualParamsPanel.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;

        if (!isManual) return;

        // Show relevant parameter sections based on method
        FragmentSizeSection.Visibility = tag == "TcpFragmentation" 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        SplitOffsetSection.Visibility = tag == "SmartFragmentation"
            ? Visibility.Visible
            : Visibility.Collapsed;
        
        TtlSection.Visibility = tag == "FakeTtl" 
            ? Visibility.Visible 
            : Visibility.Collapsed;
        
        SplitPositionSection.Visibility = tag == "Desync" 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        EchSection.Visibility = tag == "Ech" 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        QuicFakeSection.Visibility = tag == "QuicFake" 
            ? Visibility.Visible 
            : Visibility.Collapsed;

        UdpFakeTtlSection.Visibility = tag == "UdpFake"
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetSelectedMethodTag()
    {
        return (MethodCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Adaptive";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DomainBox.Text))
        {
            MessageBox.Show("Please enter a domain pattern.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Domain = DomainBox.Text.Trim();

        var methodTag = GetSelectedMethodTag();
        SelectedMethod = Enum.TryParse<BypassMethod>(methodTag, out var method) 
            ? method 
            : BypassMethod.Adaptive;

        UseAdaptiveParams = methodTag == "Adaptive" || ManualParamsToggle?.IsChecked != true;
        
        // Parse all parameters
        FragmentSize = int.TryParse(FragmentSizeBox?.Text, out var fs) ? Math.Clamp(fs, 1, 100) : 2;
        TtlValue = int.TryParse(TtlBox?.Text, out var ttl) ? Math.Clamp(ttl, 1, 64) : 3;
        SplitPosition = int.TryParse(SplitPositionBox?.Text, out var sp) ? Math.Clamp(sp, 1, 50) : 3;
        SplitOffset = int.TryParse(SplitOffsetBox?.Text, out var so) ? Math.Clamp(so, 1, 50) : 3;
        EchPublicName = EchPublicNameBox?.Text ?? "";
        QuicFakeSize = int.TryParse(QuicFakeSizeBox?.Text, out var qfs) ? Math.Clamp(qfs, 32, 256) : 64;
        UdpFakeTtl = int.TryParse(UdpFakeTtlBox?.Text, out var uft) ? Math.Clamp(uft, 1, 10) : 1;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
