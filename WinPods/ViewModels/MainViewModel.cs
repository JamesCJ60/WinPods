using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinPods.Models;
using WinPods.Services;

namespace WinPods.ViewModels;

public enum TraySummaryMode
{
    Connected,
    Pairing,
    NoDevices
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AirPodsDiscoveryService _discovery = new();
    private readonly AirPodsAdvertisementScanner _scanner = new();
    private readonly AirPodsPairingService _pairing = new();
    private readonly NearbyAirPodsPairingWatcher _pairingWatcher = new();
    private readonly BluetoothTelephonyService _bluetoothTelephony = new();
    private readonly WindowsStartupService _startupService = new();
    private readonly EqualiserProfileStore _equaliserStore = new();
    private readonly AirPodsSettingsStore _settingsStore = new();
    private readonly SystemAudioSpectrumService _spectrumService = new();
    private readonly DriverlessEqualiserService _driverlessEqualiser = new();
    private readonly WindowsSystemMediaTransport _mediaTransport;
    private readonly AirPodsWearPlaybackController _wearPlaybackController;
    private readonly AirPodsAutoSwitchController _autoSwitchController;
    private readonly AirPodsAudioConnectionState _audioConnectionState = new();
    private readonly CaseOpenSummaryGate _caseOpenSummaryGate = new();
    private readonly ThreeBandEqualiserProfile _legacyEqualiserProfile;
    private readonly Dictionary<int, AirPodsAdvertisement> _recentAdvertisements = [];
    private readonly HashSet<string> _telephonyChecksInProgress =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _caseStateTimer;
    private readonly CancellationTokenSource _connectionMonitorCancellation = new();
    private Task? _connectionMonitorTask;
    private AirPodsDevice? _selectedDevice;
    private AirPodsDevice? _summaryDevice;
    private TraySummaryMode _summaryMode = TraySummaryMode.Connected;
    private AirPodsPairingCandidate? _pairingDevice;
    private bool _isPairingNearbyDevice;
    private bool _isBusy;
    private double _lowGain;
    private double _midGain;
    private double _highGain;
    private bool _useRecommendedEqualiser;
    private bool _autoPauseEnabled = true;
    private bool _oneAirPodPlaybackEnabled;
    private bool _autoSwitchEnabled;
    private bool _bluetoothTelephonyEnabled;
    private bool? _bluetoothTelephonyPreference;
    private bool _isBluetoothTelephonyAvailable;
    private bool _isBluetoothTelephonyBusy;
    private bool _startWithWindows;
    private DriverlessEqualiserAvailability _equaliserAvailability = new(false, "Checking driverless audio routing...");
    private IReadOnlyList<double> _spectrum = new double[52];

    public MainViewModel()
    {
        _mediaTransport = new WindowsSystemMediaTransport();
        _wearPlaybackController = new AirPodsWearPlaybackController(_mediaTransport);
        _autoSwitchController = new AirPodsAutoSwitchController(_mediaTransport, _pairing);
        _scanner.AdvertisementReceived += OnAdvertisementReceived;
        _pairingWatcher.PairingDeviceFound += OnPairingDeviceFound;
        _pairingWatcher.PairingDeviceLost += OnPairingDeviceLost;
        _startWithWindows = _startupService.IsEnabled();
        _spectrumService.SpectrumAvailable += OnSpectrumAvailable;
        _caseStateTimer = new System.Threading.Timer(
            _ => UpdateOnUi(() =>
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var device in Devices) device.ExpireCaseOpenState(now);
            }), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        var profile = _legacyEqualiserProfile = _equaliserStore.Load();
        _lowGain = profile.Low;
        _midGain = profile.Mid;
        _highGain = profile.High;
        _useRecommendedEqualiser = !profile.IsCustom &&
            Math.Abs(profile.Low) < 0.05 && Math.Abs(profile.Mid) < 0.05 && Math.Abs(profile.High) < 0.05;
    }

    public ObservableCollection<AirPodsDevice> Devices { get; } = [];

    public AirPodsDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (ReferenceEquals(_selectedDevice, value)) return;
            if (_driverlessEqualiser.IsRunning)
            {
                _driverlessEqualiser.Stop();
                NotifyEqualiserStateChanged();
            }
            if (_selectedDevice is not null) _selectedDevice.PropertyChanged -= OnSelectedDeviceChanged;
            _selectedDevice = value;
            if (_selectedDevice is not null) _selectedDevice.PropertyChanged += OnSelectedDeviceChanged;
            LoadSelectedDeviceSettings();
            NotifySelectionChanged();
            RefreshEqualiserAvailability();
            EnsureCustomEqualiserRunning();
            if (_selectedDevice?.IsConnected == true)
                _ = EnforceBluetoothTelephonyAsync(_selectedDevice);
        }
    }

    public AirPodsDevice? SummaryDevice
    {
        get => _summaryDevice;
        private set => SetField(ref _summaryDevice, value);
    }

    public TraySummaryMode SummaryMode
    {
        get => _summaryMode;
        private set
        {
            if (!SetField(ref _summaryMode, value)) return;
            OnPropertyChanged(nameof(ShowPairingSummary));
            OnPropertyChanged(nameof(ShowNoDevicesSummary));
        }
    }

    public bool ShowPairingSummary => _summaryMode == TraySummaryMode.Pairing;
    public bool ShowNoDevicesSummary => _summaryMode == TraySummaryMode.NoDevices;

    public AirPodsPairingCandidate? PairingDevice
    {
        get => _pairingDevice;
        private set
        {
            if (!SetField(ref _pairingDevice, value)) return;
            OnPropertyChanged(nameof(CanPairNearbyDevice));
        }
    }

    public bool IsPairingNearbyDevice
    {
        get => _isPairingNearbyDevice;
        private set
        {
            if (!SetField(ref _isPairingNearbyDevice, value)) return;
            OnPropertyChanged(nameof(CanPairNearbyDevice));
        }
    }

    public bool CanPairNearbyDevice => PairingDevice is not null && !IsPairingNearbyDevice;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public bool HasDevice => SelectedDevice is not null;
    public bool HasNoDevices => Devices.Count == 0 && !IsBusy;
    public bool CanPairOrSwitch => HasDevice && !IsBusy;
    public string TraySummary => SelectedDevice is null
        ? "No paired AirPods"
        : $"{SelectedDevice.Name} - {SelectedDevice.DetailBatteryLabel}";

    public double LowGain
    {
        get => _lowGain;
        set => SetEqualiserGain(ref _lowGain, value);
    }

    public double MidGain
    {
        get => _midGain;
        set => SetEqualiserGain(ref _midGain, value);
    }

    public double HighGain
    {
        get => _highGain;
        set => SetEqualiserGain(ref _highGain, value);
    }

    public bool IsRecommendedEqualiser => _useRecommendedEqualiser;
    public bool IsCustomEqualiser => !_useRecommendedEqualiser;
    public bool AutoPauseEnabled
    {
        get => _autoPauseEnabled;
        set
        {
            if (!SetField(ref _autoPauseEnabled, value)) return;
            OnPropertyChanged(nameof(CanUseOneAirPodPlayback));
            SaveSelectedDeviceSettings();
        }
    }

    public bool OneAirPodPlaybackEnabled
    {
        get => _oneAirPodPlaybackEnabled;
        set
        {
            if (!SetField(ref _oneAirPodPlaybackEnabled, value)) return;
            SaveSelectedDeviceSettings();
        }
    }

    public bool AutoSwitchEnabled
    {
        get => _autoSwitchEnabled;
        set
        {
            if (!SetField(ref _autoSwitchEnabled, value)) return;
            SaveSelectedDeviceSettings();
        }
    }

    public bool BluetoothTelephonyEnabled
    {
        get => _bluetoothTelephonyEnabled;
        set
        {
            if (_bluetoothTelephonyEnabled == value && _bluetoothTelephonyPreference == value) return;
            _bluetoothTelephonyEnabled = value;
            _bluetoothTelephonyPreference = value;
            OnPropertyChanged();
            SaveSelectedDeviceSettings();
            if (SelectedDevice is not null)
                _ = EnforceBluetoothTelephonyAsync(SelectedDevice);
        }
    }

    public bool IsBluetoothTelephonyBusy
    {
        get => _isBluetoothTelephonyBusy;
        private set
        {
            if (!SetField(ref _isBluetoothTelephonyBusy, value)) return;
            OnPropertyChanged(nameof(CanToggleBluetoothTelephony));
        }
    }

    public bool CanToggleBluetoothTelephony =>
        SelectedDevice is not null && _isBluetoothTelephonyAvailable && !IsBluetoothTelephonyBusy;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            var result = _startupService.SetEnabled(value);
            _startWithWindows = result.IsEnabled;
            OnPropertyChanged();
        }
    }

    public bool CanUseOneAirPodPlayback => SelectedDevice?.IsInEar == true && AutoPauseEnabled;
    public bool IsEqualiserActive => _driverlessEqualiser.IsRunning;
    public bool CanToggleEqualiser => IsEqualiserActive ||
        (_equaliserAvailability.IsAvailable && SelectedDevice?.IsConnected == true && !IsBusy);
    public string EqualiserActionLabel => IsEqualiserActive ? "Stop EQ" : "Enable EQ";

    public IReadOnlyList<double> Spectrum
    {
        get => _spectrum;
        private set => SetField(ref _spectrum, value);
    }

    public async Task InitialiseAsync()
    {
        _spectrumService.Start();
        var mediaInitialisation = _mediaTransport.InitialiseAsync();
        try
        {
            _scanner.Start();
            _pairingWatcher.Start();
        }
        catch
        {
        }

        await RefreshAsync();
        await mediaInitialisation;
        RefreshEqualiserAvailability();
        EnsureCustomEqualiserRunning();
        _connectionMonitorTask ??= MonitorConnectionsAsync(_connectionMonitorCancellation.Token);
    }

    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        NotifyBusyStateChanged();
        try
        {
            var previousAddress = SelectedDevice?.Address;
            var previousDevices = Devices.ToDictionary(device => device.Address, StringComparer.OrdinalIgnoreCase);
            var found = await _discovery.FindPairedAsync();
            Devices.Clear();
            foreach (var device in found)
            {
                var settings = _settingsStore.Get(device.Address);
                device.RestoreDeviceColour(settings.DeviceColour);
                device.RestoreCaseBattery(settings.LastCaseBattery, settings.LastCaseBatterySeen);
                if (previousDevices.TryGetValue(device.Address, out var previous))
                    device.CopyLiveStateFrom(previous);
                Devices.Add(device);
            }
            SelectedDevice = Devices.FirstOrDefault(device => device.IsConnected)
                ?? Devices.FirstOrDefault(device => device.Address == previousAddress)
                ?? Devices.FirstOrDefault();
            foreach (var connectedDevice in Devices.Where(device =>
                         device.IsConnected && !ReferenceEquals(device, SelectedDevice)))
                _ = EnforceBluetoothTelephonyAsync(connectedDevice);
        }
        catch
        {
        }
        finally
        {
            IsBusy = false;
            NotifyBusyStateChanged();
        }
    }

    public async Task PairOrSwitchAsync()
    {
        if (SelectedDevice is null || IsBusy) return;
        IsBusy = true;
        NotifyBusyStateChanged();
        await _pairing.PairOrSwitchAsync(SelectedDevice);
        IsBusy = false;
        NotifyBusyStateChanged();
    }

    public async Task PairNearbyAsync()
    {
        var candidate = PairingDevice;
        if (candidate is null || IsPairingNearbyDevice) return;

        IsPairingNearbyDevice = true;
        var result = await _pairing.PairNearbyAsync(candidate);
        IsPairingNearbyDevice = false;
        if (!result.Success) return;

        PairingDevice = null;
        await RefreshAsync();
    }

    public void DismissNearbyPairing()
    {
        if (PairingDevice is not { } candidate) return;
        _pairingWatcher.Dismiss(candidate.Id);
        PairingDevice = null;
        SummaryMode = TraySummaryMode.Connected;
    }

    public void UseRecommendedEqualiser()
    {
        if (_driverlessEqualiser.IsRunning) _driverlessEqualiser.Stop();
        _lowGain = _midGain = _highGain = 0;
        _useRecommendedEqualiser = true;
        OnPropertyChanged(nameof(LowGain));
        OnPropertyChanged(nameof(MidGain));
        OnPropertyChanged(nameof(HighGain));
        OnPropertyChanged(nameof(IsRecommendedEqualiser));
        OnPropertyChanged(nameof(IsCustomEqualiser));
        SaveSelectedDeviceSettings();
        _driverlessEqualiser.Update(CurrentEqualiserProfile());
        RefreshEqualiserAvailability();
    }

    public void UseCustomEqualiser()
    {
        _useRecommendedEqualiser = false;
        OnPropertyChanged(nameof(IsRecommendedEqualiser));
        OnPropertyChanged(nameof(IsCustomEqualiser));
        SaveSelectedDeviceSettings();
        EnsureCustomEqualiserRunning();
    }

    public void ToggleEqualiser()
    {
        if (_driverlessEqualiser.IsRunning)
        {
            _driverlessEqualiser.Stop();
        }
        else
        {
            _useRecommendedEqualiser = false;
            OnPropertyChanged(nameof(IsRecommendedEqualiser));
            OnPropertyChanged(nameof(IsCustomEqualiser));
            _driverlessEqualiser.Start(SelectedDevice, CurrentEqualiserProfile());
        }
        NotifyEqualiserStateChanged();
        RefreshEqualiserAvailability();
    }

    public void SaveEqualiser()
    {
        var profile = CurrentEqualiserProfile();
        SaveSelectedDeviceSettings(profile);
        _driverlessEqualiser.Update(profile);
    }

    public void Dispose()
    {
        _scanner.AdvertisementReceived -= OnAdvertisementReceived;
        _scanner.Dispose();
        _pairingWatcher.PairingDeviceFound -= OnPairingDeviceFound;
        _pairingWatcher.PairingDeviceLost -= OnPairingDeviceLost;
        _pairingWatcher.Dispose();
        _spectrumService.SpectrumAvailable -= OnSpectrumAvailable;
        _spectrumService.Dispose();
        _driverlessEqualiser.Dispose();
        _caseStateTimer.Dispose();
        _connectionMonitorCancellation.Cancel();
        _connectionMonitorCancellation.Dispose();
        if (_selectedDevice is not null) _selectedDevice.PropertyChanged -= OnSelectedDeviceChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? NearbyCaseOpened;
    public event EventHandler? NearbyPairingDetected;
    public event EventHandler? DeviceConnected;

    private void SetEqualiserGain(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        var normalised = Math.Round(Math.Clamp(
            value,
            ThreeBandEqualiserProfile.MinimumGain,
            ThreeBandEqualiserProfile.MaximumGain));
        if (Math.Abs(field - normalised) < 0.05) return;
        field = normalised;
        _useRecommendedEqualiser = false;
        _driverlessEqualiser.Update(CurrentEqualiserProfile(propertyName, normalised));
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsRecommendedEqualiser));
        OnPropertyChanged(nameof(IsCustomEqualiser));
        EnsureCustomEqualiserRunning();
    }

    private void OnAdvertisementReceived(object? sender, AirPodsAdvertisement advertisement)
    {
        UpdateOnUi(() => ApplyAdvertisement(advertisement, true));
    }

    private void ApplyAdvertisement(AirPodsAdvertisement advertisement, bool allowPopup)
    {
        _recentAdvertisements[advertisement.ProductId] = advertisement;
        TryImprovePairingDeviceImage(advertisement);

        var device = AirPodsAdvertisementAssociation.FindPairedDevice(Devices, advertisement);
        if (device is null) return;

        var previousColour = device.DeviceColourCode;
        device.ApplyAdvertisement(advertisement);
        var settings = GetDeviceSettings(device);
        var shouldSaveSettings = false;
        if (device.HasColourVariants && device.DeviceColourCode != previousColour)
        {
            settings = settings with { DeviceColour = device.DeviceColourCode };
            shouldSaveSettings = true;
        }
        if (advertisement.CaseBattery.HasValue)
        {
            settings = settings with
            {
                LastCaseBattery = advertisement.CaseBattery,
                LastCaseBatterySeen = advertisement.LastSeen
            };
            shouldSaveSettings = true;
        }
        if (shouldSaveSettings) _settingsStore.Save(device.Address, settings);
        var hasAudioConnection = device.IsConnected || _audioConnectionState.IsActive(device);
        _ = _wearPlaybackController.ObserveAsync(
            device.Id,
            hasAudioConnection,
            device.IsOverEar,
            advertisement.InEarCount,
            settings);
        _ = HandleAutomaticSwitchAsync(device, advertisement, settings);
        if (ReferenceEquals(device, SelectedDevice)) NotifySelectionChanged();
        if (allowPopup && _caseOpenSummaryGate.ShouldShow(device.Id, advertisement))
        {
            SummaryDevice = device;
            SummaryMode = TraySummaryMode.Connected;
            NearbyCaseOpened?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnSpectrumAvailable(object? sender, IReadOnlyList<double> levels) =>
        UpdateOnUi(() => Spectrum = levels);

    private async Task MonitorConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
                var found = await _discovery.FindPairedAsync();
                UpdateOnUi(() =>
                {
                    AirPodsDevice? newlyConnected = null;
                    foreach (var fresh in found)
                    {
                        var existing = Devices.FirstOrDefault(device =>
                            string.Equals(device.Address, fresh.Address, StringComparison.OrdinalIgnoreCase));
                        if (existing is null)
                        {
                            var settings = _settingsStore.Get(fresh.Address);
                            fresh.RestoreDeviceColour(settings.DeviceColour);
                            fresh.RestoreCaseBattery(settings.LastCaseBattery, settings.LastCaseBatterySeen);
                            Devices.Add(fresh);
                            SelectedDevice ??= fresh;
                            if (fresh.IsConnected)
                            {
                                newlyConnected = fresh;
                                SummaryDevice = fresh;
                                SummaryMode = TraySummaryMode.Connected;
                                DeviceConnected?.Invoke(this, EventArgs.Empty);
                                _ = EnforceBluetoothTelephonyAsync(fresh);
                            }
                            continue;
                        }

                        var wasConnected = existing.IsConnected;
                        existing.IsConnected = fresh.IsConnected;
                        if (!wasConnected && existing.IsConnected)
                        {
                            newlyConnected = existing;
                            SummaryDevice = existing;
                            SummaryMode = TraySummaryMode.Connected;
                            DeviceConnected?.Invoke(this, EventArgs.Empty);
                            _ = EnforceBluetoothTelephonyAsync(existing);
                        }
                    }

                    var connected = newlyConnected ?? Devices.FirstOrDefault(device => device.IsConnected);
                    if (connected is not null && !ReferenceEquals(SelectedDevice, connected))
                        SelectedDevice = connected;
                    foreach (var connectedDevice in Devices.Where(device => device.IsConnected))
                        _ = EnforceBluetoothTelephonyAsync(connectedDevice);
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
            }
        }
    }

    private void OnSelectedDeviceChanged(object? sender, PropertyChangedEventArgs args)
    {
        NotifySelectionChanged();
        if (args.PropertyName != nameof(AirPodsDevice.IsConnected)) return;
        if (SelectedDevice?.IsConnected != true && _driverlessEqualiser.IsRunning)
            _driverlessEqualiser.Stop();
        RefreshEqualiserAvailability();
        if (SelectedDevice?.IsConnected == true) EnsureCustomEqualiserRunning();
    }

    public bool ShowConnectedInSummary()
    {
        var connected = Devices.FirstOrDefault(device => device.IsConnected);
        if (connected is null) return false;
        SummaryDevice = connected;
        SummaryMode = TraySummaryMode.Connected;
        return true;
    }

    public void PrepareManualSummary()
    {
        if (Devices.FirstOrDefault(device => device.IsConnected) is { } connected)
        {
            SummaryDevice = connected;
            SummaryMode = TraySummaryMode.Connected;
        }
        else if (PairingDevice is not null)
            SummaryMode = TraySummaryMode.Pairing;
        else
            SummaryMode = TraySummaryMode.NoDevices;
    }

    private void OnPairingDeviceFound(object? sender, AirPodsPairingCandidate candidate)
    {
        UpdateOnUi(() =>
        {
            var advertisement = _recentAdvertisements.Values
                .Where(item => DateTimeOffset.UtcNow - item.LastSeen < TimeSpan.FromSeconds(12))
                .Where(item => IsSameAirPodsFamily(candidate.Name, item.Model))
                .OrderByDescending(item => item.LastSeen)
                .FirstOrDefault();
            var enriched = advertisement is null
                ? candidate
                : candidate with
                {
                    ProductId = advertisement.ProductId,
                    DeviceColour = advertisement.DeviceColour
                };

            if (IsAlreadyPairedDevice(enriched))
            {
                if (string.Equals(PairingDevice?.Id, enriched.Id, StringComparison.OrdinalIgnoreCase))
                    ClearPairingDevice();
                return;
            }

            var isNewDevice = !string.Equals(PairingDevice?.Id, enriched.Id, StringComparison.OrdinalIgnoreCase);
            PairingDevice = enriched;
            if (isNewDevice)
            {
                SummaryMode = TraySummaryMode.Pairing;
                NearbyPairingDetected?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private bool IsAlreadyPairedDevice(AirPodsPairingCandidate candidate) =>
        Devices.Any(device =>
            (candidate.ContainerId is { } container && device.ContainerId == container) ||
            string.Equals(device.Name, candidate.Name, StringComparison.OrdinalIgnoreCase));

    private void ClearPairingDevice()
    {
        PairingDevice = null;
        if (SummaryMode == TraySummaryMode.Pairing) SummaryMode = TraySummaryMode.Connected;
    }

    private void TryImprovePairingDeviceImage(AirPodsAdvertisement advertisement)
    {
        if (PairingDevice is not { } candidate ||
            !IsSameAirPodsFamily(candidate.Name, advertisement.Model))
            return;

        PairingDevice = candidate with
        {
            ProductId = advertisement.ProductId,
            DeviceColour = advertisement.DeviceColour
        };
    }

    private void OnPairingDeviceLost(object? sender, string id)
    {
        UpdateOnUi(() =>
        {
            if (string.Equals(PairingDevice?.Id, id, StringComparison.OrdinalIgnoreCase))
                PairingDevice = null;
        });
    }

    private static bool IsSameAirPodsFamily(string name, string model)
    {
        if (name.Contains("Beats", StringComparison.OrdinalIgnoreCase) !=
            model.Contains("Beats", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.Contains("Max", StringComparison.OrdinalIgnoreCase))
            return model.Contains("Max", StringComparison.OrdinalIgnoreCase);
        if (name.Contains("Pro", StringComparison.OrdinalIgnoreCase))
            return model.Contains("Pro", StringComparison.OrdinalIgnoreCase);
        return !model.Contains("Max", StringComparison.OrdinalIgnoreCase) &&
               !model.Contains("Pro", StringComparison.OrdinalIgnoreCase);
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedDevice));
        OnPropertyChanged(nameof(HasDevice));
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(CanPairOrSwitch));
        OnPropertyChanged(nameof(TraySummary));
        OnPropertyChanged(nameof(CanUseOneAirPodPlayback));
        OnPropertyChanged(nameof(CanToggleBluetoothTelephony));
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanPairOrSwitch));
        OnPropertyChanged(nameof(HasNoDevices));
        OnPropertyChanged(nameof(CanToggleEqualiser));
    }

    private ThreeBandEqualiserProfile CurrentEqualiserProfile(string? changedProperty = null, double changedValue = 0) =>
        new(
            changedProperty == nameof(LowGain) ? changedValue : LowGain,
            changedProperty == nameof(MidGain) ? changedValue : MidGain,
            changedProperty == nameof(HighGain) ? changedValue : HighGain)
        {
            IsCustom = !_useRecommendedEqualiser
        };

    private AirPodsDeviceSettings GetDeviceSettings(AirPodsDevice device) =>
        ReferenceEquals(device, SelectedDevice)
            ? CurrentDeviceSettings()
            : _settingsStore.Get(device.Address);

    private AirPodsDeviceSettings CurrentDeviceSettings(ThreeBandEqualiserProfile? equaliser = null) => new()
    {
        AutoPause = AutoPauseEnabled,
        OneAirPodPlayback = OneAirPodPlaybackEnabled,
        AutoSwitch = AutoSwitchEnabled,
        BluetoothTelephonyEnabled = _bluetoothTelephonyPreference,
        LastCaseBattery = SelectedDevice?.CaseBattery,
        LastCaseBatterySeen = SelectedDevice?.CaseBatteryLastSeen,
        DeviceColour = SelectedDevice?.DeviceColourCode,
        Equaliser = equaliser ?? CurrentEqualiserProfile()
    };

    private string SaveSelectedDeviceSettings(ThreeBandEqualiserProfile? equaliser = null) =>
        SelectedDevice is null
            ? "No AirPods are selected."
            : _settingsStore.Save(SelectedDevice.Address, CurrentDeviceSettings(equaliser));

    private void LoadSelectedDeviceSettings()
    {
        var settings = SelectedDevice is null
            ? new AirPodsDeviceSettings { Equaliser = _legacyEqualiserProfile }
            : _settingsStore.Get(SelectedDevice.Address, _legacyEqualiserProfile);
        SelectedDevice?.RestoreDeviceColour(settings.DeviceColour);
        SelectedDevice?.RestoreCaseBattery(settings.LastCaseBattery, settings.LastCaseBatterySeen);
        _autoPauseEnabled = settings.AutoPause;
        _oneAirPodPlaybackEnabled = settings.OneAirPodPlayback;
        _autoSwitchEnabled = settings.AutoSwitch;
        _bluetoothTelephonyPreference = settings.BluetoothTelephonyEnabled;
        var telephonyState = SelectedDevice is null
            ? new BluetoothTelephonyState(false, false, string.Empty)
            : _bluetoothTelephony.Read(SelectedDevice);
        _isBluetoothTelephonyAvailable = telephonyState.IsAvailable;
        _bluetoothTelephonyEnabled = telephonyState.IsEnabled;
        _isBluetoothTelephonyBusy = false;
        _lowGain = settings.Equaliser.Low;
        _midGain = settings.Equaliser.Mid;
        _highGain = settings.Equaliser.High;
        _useRecommendedEqualiser = !settings.Equaliser.IsCustom;
        OnPropertyChanged(nameof(AutoPauseEnabled));
        OnPropertyChanged(nameof(OneAirPodPlaybackEnabled));
        OnPropertyChanged(nameof(AutoSwitchEnabled));
        OnPropertyChanged(nameof(BluetoothTelephonyEnabled));
        OnPropertyChanged(nameof(IsBluetoothTelephonyBusy));
        OnPropertyChanged(nameof(CanToggleBluetoothTelephony));
        OnPropertyChanged(nameof(CanUseOneAirPodPlayback));
        OnPropertyChanged(nameof(LowGain));
        OnPropertyChanged(nameof(MidGain));
        OnPropertyChanged(nameof(HighGain));
        OnPropertyChanged(nameof(IsRecommendedEqualiser));
        OnPropertyChanged(nameof(IsCustomEqualiser));
    }

    private async Task EnforceBluetoothTelephonyAsync(AirPodsDevice device)
    {
        var desired = _settingsStore.Get(device.Address).BluetoothTelephonyEnabled;
        if (!desired.HasValue) return;
        if (!_telephonyChecksInProgress.Add(device.Address)) return;

        var selected = ReferenceEquals(device, SelectedDevice);
        if (selected) UpdateOnUi(() => IsBluetoothTelephonyBusy = true);
        try
        {
            var result = await _bluetoothTelephony.EnsureAsync(
                device,
                desired.Value,
                _connectionMonitorCancellation.Token);
            UpdateOnUi(() =>
            {
                if (ReferenceEquals(device, SelectedDevice))
                {
                    _isBluetoothTelephonyAvailable = result.Success || result.Changed;
                    _bluetoothTelephonyEnabled = result.IsEnabled;
                    OnPropertyChanged(nameof(BluetoothTelephonyEnabled));
                    OnPropertyChanged(nameof(CanToggleBluetoothTelephony));
                }
            });
        }
        catch (OperationCanceledException) when (_connectionMonitorCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            UpdateOnUi(() =>
            {
                _telephonyChecksInProgress.Remove(device.Address);
                if (ReferenceEquals(device, SelectedDevice))
                    IsBluetoothTelephonyBusy = false;
            });
        }
    }

    private async Task HandleAutomaticSwitchAsync(
        AirPodsDevice device,
        AirPodsAdvertisement advertisement,
        AirPodsDeviceSettings settings)
    {
        await _autoSwitchController.ObserveAsync(device, advertisement, settings);
    }

    private void RefreshEqualiserAvailability()
    {
        if (!_driverlessEqualiser.IsRunning)
            _equaliserAvailability = _driverlessEqualiser.Probe(SelectedDevice);
        NotifyEqualiserStateChanged();
    }

    private void NotifyEqualiserStateChanged()
    {
        OnPropertyChanged(nameof(IsEqualiserActive));
        OnPropertyChanged(nameof(CanToggleEqualiser));
        OnPropertyChanged(nameof(EqualiserActionLabel));
    }

    private void EnsureCustomEqualiserRunning()
    {
        if (_useRecommendedEqualiser || _driverlessEqualiser.IsRunning ||
            SelectedDevice?.IsConnected != true || IsBusy)
            return;

        _driverlessEqualiser.Start(SelectedDevice, CurrentEqualiserProfile());
        RefreshEqualiserAvailability();
    }

    private static void UpdateOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(action);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
