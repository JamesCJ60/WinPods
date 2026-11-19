namespace WinPods.Models;

public sealed record AirPodsAdvertisement(
    string Address,
    int ProductId,
    string Model,
    int? LeftBattery,
    int? RightBattery,
    int? CaseBattery,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    bool IsCaseOpen,
    bool? IsBeingWorn,
    int? InEarCount,
    BudLocation LeftBud,
    BudLocation RightBud,
    byte DeviceColour,
    byte Status,
    DateTimeOffset LastSeen);
