using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinPods.Models;

public sealed class AirPodsDevice : INotifyPropertyChanged
{
    // Buds stop advertising once the case lid is closed, so "in case" is kept until a newer
    // advertisement says otherwise; in-ear/out-of-ear readings are dropped once they go stale.
    private static readonly TimeSpan WearStateLifetime = TimeSpan.FromSeconds(30);

    private string _name = "AirPods";
    private string _model = "AirPods";
    private bool _isConnected;
    private int? _leftBattery;
    private int? _rightBattery;
    private int? _caseBattery;
    private bool _isLeftCharging;
    private bool _isRightCharging;
    private bool _isCaseCharging;
    private bool _isCaseOpen;
    private DateTimeOffset? _lastSeen;
    private DateTimeOffset? _lastCaseOpenSeen;
    private DateTimeOffset? _caseBatteryLastSeen;
    private byte? _deviceColourCode;
    private BudLocation _leftBud;
    private BudLocation _rightBud;
    private bool? _isHeadsetWorn;
    private DateTimeOffset? _wearStateLastSeen;
    private bool _hasAudioEndpoint;
    private bool _isAudioInUse;

    public required string Id { get; init; }

    public required string Address { get; init; }

    public int ProductId { get; init; }

    public Guid? ContainerId { get; init; }

    public byte? DeviceColourCode
    {
        get => _deviceColourCode;
        private set
        {
            if (SetField(ref _deviceColourCode, value))
                OnPropertyChanged(nameof(ProductImagePath));
        }
    }

    public string ProductImagePath => AirPodsProductCatalogue.GetProductImagePath(ProductId, DeviceColourCode);

    public System.Windows.Rect ProductImageViewbox => AirPodsProductCatalogue.GetProductImageViewbox(ProductId);

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Model
    {
        get => _model;
        set
        {
            if (SetField(ref _model, value))
            {
                OnPropertyChanged(nameof(IsOverEar));
                OnPropertyChanged(nameof(IsInEar));
                OnPropertyChanged(nameof(DetailBatteryLabel));
                NotifyLiveStateChanged();
            }
        }
    }

    /// <summary>True while Windows reports a Bluetooth link to the AirPods (says nothing about audio).</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            var previousStatus = ConnectionStatus;
            if (SetField(ref _isConnected, value) && previousStatus != ConnectionStatus)
                NotifyConnectionStatusChanged();
        }
    }

    public AirPodsConnectionStatus ConnectionStatus =>
        _isAudioInUse ? AirPodsConnectionStatus.Active
        : _hasAudioEndpoint ? AirPodsConnectionStatus.Ready
        : IsConnected ? AirPodsConnectionStatus.AudioUnavailable
        : AirPodsConnectionStatus.NotConnected;

    public bool IsAudioActive => ConnectionStatus == AirPodsConnectionStatus.Active;

    public BudLocation LeftBud
    {
        get => _leftBud;
        private set
        {
            if (!SetField(ref _leftBud, value)) return;
            OnPropertyChanged(nameof(LeftBudLabel));
            NotifyLiveStateChanged();
        }
    }

    public BudLocation RightBud
    {
        get => _rightBud;
        private set
        {
            if (!SetField(ref _rightBud, value)) return;
            OnPropertyChanged(nameof(RightBudLabel));
            NotifyLiveStateChanged();
        }
    }

    public string LeftBudLabel => FormatBudLocation(LeftBud);

    public string RightBudLabel => FormatBudLocation(RightBud);

    /// <summary>Over-ear only: whether the headphones are on someone's head (null when unknown).</summary>
    public bool? IsHeadsetWorn
    {
        get => _isHeadsetWorn;
        private set
        {
            if (!SetField(ref _isHeadsetWorn, value)) return;
            OnPropertyChanged(nameof(HeadsetWearLabel));
            OnPropertyChanged(nameof(HasHeadsetWearState));
        }
    }

    public string HeadsetWearLabel => IsHeadsetWorn switch
    {
        true => "Worn",
        false => "Not worn",
        null => string.Empty
    };

    public bool HasHeadsetWearState => IsOverEar && IsHeadsetWorn.HasValue;

    public bool HasChargingCase => !IsOverEar && (ProductId == 0 || AirPodsProductCatalogue.HasChargingCase(ProductId));

    /// <summary>Show the per-bud grid once there is anything to put in it.</summary>
    public bool ShowsEarbudGrid =>
        IsInEar &&
        (LeftBattery.HasValue || RightBattery.HasValue || CaseBattery.HasValue ||
         LeftBud != BudLocation.Unknown || RightBud != BudLocation.Unknown);

    /// <summary>Single-line fallback: headset battery, or the "open the case" hint.</summary>
    public bool ShowsBatterySummary => !ShowsEarbudGrid;

    public int? LeftBattery
    {
        get => _leftBattery;
        set
        {
            if (SetField(ref _leftBattery, value))
            {
                NotifyBatteryChanged();
            }
        }
    }

    public int? RightBattery
    {
        get => _rightBattery;
        set
        {
            if (SetField(ref _rightBattery, value))
            {
                NotifyBatteryChanged();
            }
        }
    }

    public int? CaseBattery
    {
        get => _caseBattery;
        set
        {
            if (SetField(ref _caseBattery, value))
            {
                OnPropertyChanged(nameof(CaseBatteryLabel));
                OnPropertyChanged(nameof(CaseBatteryValue));
                OnPropertyChanged(nameof(DetailBatteryLabel));
                NotifyLiveStateChanged();
            }
        }
    }

    public bool IsCaseOpen
    {
        get => _isCaseOpen;
        private set
        {
            if (SetField(ref _isCaseOpen, value))
                OnPropertyChanged(nameof(NearbyStatusLabel));
        }
    }

    public DateTimeOffset? LastSeen
    {
        get => _lastSeen;
        private set => SetField(ref _lastSeen, value);
    }

    public bool IsOverEar =>
        Model.Contains("Max", StringComparison.OrdinalIgnoreCase) ||
        AirPodsProductCatalogue.IsOverEar(ProductId);

    public bool IsInEar => !IsOverEar;

    public bool HasColourVariants => AirPodsProductCatalogue.HasColourVariants(ProductId);

    public int? DisplayBattery
    {
        get
        {
            var levels = new[] { LeftBattery, RightBattery }.Where(level => level.HasValue).Select(level => level!.Value).ToArray();
            return levels.Length == 0 ? null : (int)Math.Round(levels.Average());
        }
    }

    public string BatteryLabel => DisplayBattery is int level ? $"{level}%" : "—";

    public double LeftBatteryValue => LeftBattery ?? 0;

    public double RightBatteryValue => RightBattery ?? 0;

    public double CaseBatteryValue => CaseBattery ?? 0;

    public string LeftBatteryLabel => FormatBattery(LeftBattery, _isLeftCharging);

    public string RightBatteryLabel => FormatBattery(RightBattery, _isRightCharging);

    public string CaseBatteryLabel => FormatBattery(CaseBattery, _isCaseCharging);

    public DateTimeOffset? CaseBatteryLastSeen => _caseBatteryLastSeen;

    public string DetailBatteryLabel
    {
        get
        {
            if (IsOverEar && DisplayBattery is int headset) return $"Headset {headset}%";

            var parts = new List<string>();
            if (LeftBattery is int left) parts.Add($"Left {left}%");
            if (RightBattery is int right) parts.Add($"Right {right}%");
            if (CaseBattery is int chargingCase) parts.Add($"Case {chargingCase}%");
            return parts.Count == 0 ? "Open the case to read battery levels" : string.Join("  •  ", parts);
        }
    }

    public string ConnectionLabel => ConnectionStatus switch
    {
        AirPodsConnectionStatus.Active => "Active on this PC",
        AirPodsConnectionStatus.Ready => "Connected  ·  not in use",
        AirPodsConnectionStatus.AudioUnavailable => "Connected  ·  audio not ready",
        _ => "Not connected"
    };

    public string NearbyStatusLabel => IsCaseOpen ? $"Case open  ·  {ConnectionLabel}" : ConnectionLabel;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Records what Windows audio is doing with these AirPods: whether their audio endpoint is
    /// active, and whether it is the one Windows is currently playing through.
    /// </summary>
    public void ApplyAudioRoute(bool hasActiveEndpoint, bool isInUse)
    {
        var previousStatus = ConnectionStatus;
        _hasAudioEndpoint = hasActiveEndpoint;
        _isAudioInUse = hasActiveEndpoint && isInUse;
        if (previousStatus != ConnectionStatus) NotifyConnectionStatusChanged();
    }

    public void ApplyAdvertisement(AirPodsAdvertisement advertisement)
    {
        Model = advertisement.Model;
        LastSeen = advertisement.LastSeen;
        if (HasColourVariants) DeviceColourCode = advertisement.DeviceColour;
        if (IsOverEar)
        {
            if (advertisement.InEarCount is int worn) IsHeadsetWorn = worn > 0;
        }
        else
        {
            LeftBud = advertisement.LeftBud;
            RightBud = advertisement.RightBud;
        }
        _wearStateLastSeen = advertisement.LastSeen;
        if (advertisement.IsCaseOpen)
        {
            _lastCaseOpenSeen = advertisement.LastSeen;
            IsCaseOpen = true;
        }
        if (advertisement.LeftBattery.HasValue)
        {
            _isLeftCharging = advertisement.LeftCharging;
            LeftBattery = advertisement.LeftBattery;
        }
        if (advertisement.RightBattery.HasValue)
        {
            _isRightCharging = advertisement.RightCharging;
            RightBattery = advertisement.RightBattery;
        }
        if (advertisement.CaseBattery.HasValue)
        {
            _isCaseCharging = advertisement.CaseCharging;
            _caseBatteryLastSeen = advertisement.LastSeen;
            CaseBattery = advertisement.CaseBattery;
            OnPropertyChanged(nameof(DetailBatteryLabel));
        }
        OnPropertyChanged(nameof(LeftBatteryLabel));
        OnPropertyChanged(nameof(RightBatteryLabel));
        OnPropertyChanged(nameof(CaseBatteryLabel));
    }

    public void CopyLiveStateFrom(AirPodsDevice other)
    {
        _isLeftCharging = other._isLeftCharging;
        _isRightCharging = other._isRightCharging;
        _isCaseCharging = other._isCaseCharging;
        _caseBatteryLastSeen = other._caseBatteryLastSeen;
        _wearStateLastSeen = other._wearStateLastSeen;
        LeftBud = other.LeftBud;
        RightBud = other.RightBud;
        IsHeadsetWorn = other.IsHeadsetWorn;
        ApplyAudioRoute(other._hasAudioEndpoint, other._isAudioInUse);
        if (other.LastSeen.HasValue) LastSeen = other.LastSeen;
        if (other.LeftBattery.HasValue) LeftBattery = other.LeftBattery;
        if (other.RightBattery.HasValue) RightBattery = other.RightBattery;
        if (other.CaseBattery.HasValue) CaseBattery = other.CaseBattery;
        OnPropertyChanged(nameof(DetailBatteryLabel));
        OnPropertyChanged(nameof(LeftBatteryLabel));
        OnPropertyChanged(nameof(RightBatteryLabel));
        OnPropertyChanged(nameof(CaseBatteryLabel));
    }

    public void RestoreDeviceColour(byte? colour)
    {
        if (HasColourVariants && colour.HasValue) DeviceColourCode = colour;
    }

    public void RestoreCaseBattery(int? battery, DateTimeOffset? lastSeen)
    {
        if (IsOverEar ||
            battery is not (>= 0 and <= 100) ||
            lastSeen is null ||
            DateTimeOffset.UtcNow - lastSeen > TimeSpan.FromHours(24))
            return;

        _caseBatteryLastSeen = lastSeen;
        CaseBattery = battery;
        OnPropertyChanged(nameof(DetailBatteryLabel));
    }

    public void ExpireCaseOpenState(DateTimeOffset now)
    {
        if (IsCaseOpen && _lastCaseOpenSeen is { } lastSeen && now - lastSeen > TimeSpan.FromSeconds(6))
            IsCaseOpen = false;
    }

    public void ExpireWearState(DateTimeOffset now)
    {
        if (_wearStateLastSeen is not { } lastSeen || now - lastSeen <= WearStateLifetime) return;
        if (LeftBud != BudLocation.InCase) LeftBud = BudLocation.Unknown;
        if (RightBud != BudLocation.InCase) RightBud = BudLocation.Unknown;
        IsHeadsetWorn = null;
    }

    private static string FormatBudLocation(BudLocation location) => location switch
    {
        BudLocation.InEar => "In ear",
        BudLocation.OutOfEar => "Out of ear",
        BudLocation.InCase => "In case",
        _ => "—"
    };

    private void NotifyConnectionStatusChanged()
    {
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(IsAudioActive));
        OnPropertyChanged(nameof(ConnectionLabel));
        OnPropertyChanged(nameof(NearbyStatusLabel));
    }

    private void NotifyLiveStateChanged()
    {
        OnPropertyChanged(nameof(ShowsEarbudGrid));
        OnPropertyChanged(nameof(ShowsBatterySummary));
        OnPropertyChanged(nameof(HasChargingCase));
        OnPropertyChanged(nameof(HasHeadsetWearState));
    }

    private void NotifyBatteryChanged()
    {
        OnPropertyChanged(nameof(DisplayBattery));
        OnPropertyChanged(nameof(BatteryLabel));
        OnPropertyChanged(nameof(DetailBatteryLabel));
        OnPropertyChanged(nameof(LeftBatteryLabel));
        OnPropertyChanged(nameof(RightBatteryLabel));
        OnPropertyChanged(nameof(LeftBatteryValue));
        OnPropertyChanged(nameof(RightBatteryValue));
        NotifyLiveStateChanged();
    }

    private static string FormatBattery(int? level, bool charging) =>
        level is int value ? $"{value}%{(charging ? "  ⚡" : string.Empty)}" : "—";

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
