using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace NetworkAnalyzer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly ICollectionView _interfacesView;
    private InterfaceInfo? _selectedInterface;
    private string? _interfaceFilter;
    private string _urlScheme = "N/A";
    private string _urlHost = "N/A";
    private string _urlPort = "N/A";
    private string _urlPath = "N/A";
    private string _urlQuery = "N/A";
    private string _urlFragment = "N/A";
    private bool _isUrlValid = true;
    private string _urlValidationMessage = "Enter a valid absolute URL, e.g., https://example.com/path";

    public ObservableCollection<InterfaceInfo> Interfaces { get; } = new();
    public ObservableCollection<string> OutputLines { get; } = new();
    public ObservableCollection<string> UrlHistory { get; } = new();

    public ICollectionView InterfacesView => _interfacesView;

    public InterfaceInfo? SelectedInterface
    {
        get => _selectedInterface;
        set
        {
            if (Equals(value, _selectedInterface)) return;
            _selectedInterface = value;
            OnPropertyChanged();
        }
    }

    public string InterfaceFilter
    {
        get => _interfaceFilter ?? string.Empty;
        set
        {
            if (value == _interfaceFilter) return;
            _interfaceFilter = value;
            OnPropertyChanged();
            _interfacesView.Refresh();
        }
    }

    public string UrlScheme { get => _urlScheme; set => SetField(ref _urlScheme, value); }
    public string UrlHost { get => _urlHost; set => SetField(ref _urlHost, value); }
    public string UrlPort { get => _urlPort; set => SetField(ref _urlPort, value); }
    public string UrlPath { get => _urlPath; set => SetField(ref _urlPath, value); }
    public string UrlQuery { get => _urlQuery; set => SetField(ref _urlQuery, value); }
    public string UrlFragment { get => _urlFragment; set => SetField(ref _urlFragment, value); }
    public bool IsUrlValid { get => _isUrlValid; set => SetField(ref _isUrlValid, value); }
    public string UrlValidationMessage { get => _urlValidationMessage; set => SetField(ref _urlValidationMessage, value); }

    public MainWindow()
    {
        InitializeComponent();

        LoadInterfaces();
        _interfacesView = CollectionViewSource.GetDefaultView(Interfaces);
        _interfacesView.Filter = FilterInterfaces;
        _interfacesView.Refresh();

        LoadHistory();
        SelectedInterface = Interfaces.FirstOrDefault();
        DataContext = this;
    }

    private bool FilterInterfaces(object obj)
    {
        if (obj is not InterfaceInfo info) return false;
        if (string.IsNullOrWhiteSpace(_interfaceFilter)) return true;
        var filter = _interfaceFilter.Trim();
        return info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || info.Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadInterfaces()
    {
        Interfaces.Clear();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            var ipProps = ni.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            var ip = ipv4?.Address.ToString() ?? "N/A";
            var mask = ipv4?.IPv4Mask?.ToString() ?? "N/A";

            var mac = FormatMac(ni.GetPhysicalAddress());
            var status = ni.OperationalStatus.ToString();
            var speed = FormatSpeed(ni.Speed);
            var type = ni.NetworkInterfaceType.ToString();

            Interfaces.Add(new InterfaceInfo(
                ni.Name,
                ni.Description,
                ip,
                mask,
                mac,
                status,
                speed,
                type));
        }
    }

    private static string FormatMac(PhysicalAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 0) return "N/A";
        return string.Join("-", bytes.Select(b => b.ToString("X2")));
    }

    private static string FormatSpeed(long speedBits)
    {
        if (speedBits <= 0) return "N/A";
        var mbps = speedBits / 1_000_000d;
        return $"{mbps:0.##} Mbps";
    }

    private void ParseUrl_Click(object sender, RoutedEventArgs e)
    {
        var raw = UrlInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            MarkUrlInvalid("URL is empty.");
            ClearUrlFields();
            AddOutput("URL is empty.");
            return;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            if (!IsValidHost(raw))
            {
                MarkUrlInvalid("Invalid URL. Use absolute form like https://example.com/path");
                ClearUrlFields();
                AddOutput("Invalid URL. Use absolute form like https://example.com/path");
                return;
            }

            MarkUrlValid("Host looks valid (no scheme).");
            UrlScheme = "N/A";
            UrlHost = raw;
            UrlPort = "N/A";
            UrlPath = "N/A";
            UrlQuery = "N/A";
            UrlFragment = "N/A";
            AddToHistory(raw);
            AddOutput($"Parsed host: {raw}");
            return;
        }

        MarkUrlValid("URL looks valid.");
        UrlScheme = uri.Scheme;
        UrlHost = uri.Host;
        UrlPort = uri.IsDefaultPort ? "default" : uri.Port.ToString();
        UrlPath = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "N/A" : uri.AbsolutePath;
        UrlQuery = string.IsNullOrWhiteSpace(uri.Query) ? "N/A" : uri.Query;
        UrlFragment = string.IsNullOrWhiteSpace(uri.Fragment) ? "N/A" : uri.Fragment;

        AddToHistory(raw);
        AddOutput($"Parsed URL: {uri.Host} ({uri.Scheme})");
    }

    private async void Ping_Click(object sender, RoutedEventArgs e)
    {
        var host = GetHostFromInput();
        if (string.IsNullOrWhiteSpace(host))
        {
            AddOutput("Ping: host is empty.");
            return;
        }

        try
        {
            if (!IsValidHost(host))
            {
                AddOutput("Ping: invalid host.");
                return;
            }
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            var status = reply.Status.ToString();
            var rtt = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : "N/A";
            AddOutput($"Ping {host}: {status}, RTT {rtt}");
        }
        catch (Exception ex)
        {
            AddOutput($"Ping failed: {ex.Message}");
        }
    }

    private async void Dns_Click(object sender, RoutedEventArgs e)
    {
        var host = GetHostFromInput();
        if (string.IsNullOrWhiteSpace(host))
        {
            AddOutput("DNS: host is empty.");
            return;
        }

        try
        {
            if (!IsValidHost(host))
            {
                AddOutput("DNS: invalid host.");
                return;
            }
            var entry = await Dns.GetHostEntryAsync(host);
            AddOutput($"DNS for {host}:");
            foreach (var ip in entry.AddressList)
            {
                AddOutput($"  {ip} [{GetAddressType(ip)}]");
            }
        }
        catch (Exception ex)
        {
            AddOutput($"DNS failed: {ex.Message}");
        }
    }

    private string? GetHostFromInput()
    {
        var raw = UrlInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            MarkUrlValid();
            AddToHistory(raw);
            return uri.Host;
        }

        AddToHistory(raw);
        return raw;
    }

    private static bool IsValidHost(string host)
    {
        if (IPAddress.TryParse(host, out _)) return true;
        return Uri.CheckHostName(host) != UriHostNameType.Unknown;
    }

    private static string GetAddressType(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return "loopback";

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes[0] == 10) return "local";
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return "local";
            if (bytes[0] == 192 && bytes[1] == 168) return "local";
            if (bytes[0] == 169 && bytes[1] == 254) return "local (link-local)";
            return "public";
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return "local";
            if (IsUniqueLocal(address)) return "local (ULA)";
            return "public";
        }

        return "unknown";
    }

    private static bool IsUniqueLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length > 0 && (bytes[0] & 0b1111_1110) == 0b1111_1100;
    }

    private void AddOutput(string message)
    {
        OutputLines.Insert(0, $"{DateTime.Now:HH:mm:ss} {message}");
        if (OutputLines.Count > 200)
        {
            OutputLines.RemoveAt(OutputLines.Count - 1);
        }
    }

    private void AddToHistory(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (UrlHistory.Contains(url))
        {
            UrlHistory.Remove(url);
        }
        UrlHistory.Insert(0, url);
        if (UrlHistory.Count > 50)
        {
            UrlHistory.RemoveAt(UrlHistory.Count - 1);
        }
        SaveHistory();
    }

    private string HistoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkAnalyzer", "url_history.json");

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return;
            var json = File.ReadAllText(HistoryPath);
            var items = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            UrlHistory.Clear();
            foreach (var item in items)
            {
                UrlHistory.Add(item);
            }
        }
        catch
        {
            UrlHistory.Clear();
        }
    }

    private void SaveHistory()
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(UrlHistory.ToList());
            File.WriteAllText(HistoryPath, json);
        }
        catch
        {
            AddOutput("Failed to save URL history.");
        }
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not string url) return;
        UrlInput.Text = url;
    }

    private void ClearResults_Click(object sender, RoutedEventArgs e)
    {
        OutputLines.Clear();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        UrlHistory.Clear();
        SaveHistory();
    }

    private void MarkUrlInvalid(string message)
    {
        IsUrlValid = false;
        UrlValidationMessage = message;
    }

    private void MarkUrlValid(string message = "URL looks valid.")
    {
        IsUrlValid = true;
        UrlValidationMessage = message;
    }

    private void ClearUrlFields()
    {
        UrlScheme = "N/A";
        UrlHost = "N/A";
        UrlPort = "N/A";
        UrlPath = "N/A";
        UrlQuery = "N/A";
        UrlFragment = "N/A";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(name);
    }
}

public sealed class InterfaceInfo
{
    public InterfaceInfo(
        string name,
        string description,
        string ipAddress,
        string subnetMask,
        string macAddress,
        string status,
        string speed,
        string type)
    {
        Name = name;
        Description = description;
        IpAddress = ipAddress;
        SubnetMask = subnetMask;
        MacAddress = macAddress;
        Status = status;
        Speed = speed;
        Type = type;
    }

    public string Name { get; }
    public string Description { get; }
    public string IpAddress { get; }
    public string SubnetMask { get; }
    public string MacAddress { get; }
    public string Status { get; }
    public string Speed { get; }
    public string Type { get; }
    public string SpeedAndType => $"{Speed} / {Type}";
}
