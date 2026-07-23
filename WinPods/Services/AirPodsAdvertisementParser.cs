using WinPods.Models;

namespace WinPods.Services;

public static class AirPodsAdvertisementParser
{
    public static bool TryParse(string address, ReadOnlySpan<byte> data, out AirPodsAdvertisement? advertisement)
    {
        advertisement = null;
        if (data.Length < 11 || data[0] != 0x07)
        {
            return false;
        }

        var modelId = (data[3] << 8) | data[4];
        var model = AirPodsProductCatalogue.GetModelName(modelId, $"AirPods ({modelId:X4})");
        var status = data[5];
        var batteries = data[6];
        var flagsAndCase = data[7];
        var primaryLeft = (status & 0x20) != 0;
        var flipped = !primaryLeft;

        var leftNibble = flipped ? (batteries >> 4) & 0x0F : batteries & 0x0F;
        var rightNibble = flipped ? batteries & 0x0F : (batteries >> 4) & 0x0F;
        var caseNibble = flagsAndCase & 0x0F;
        var flags = (flagsAndCase >> 4) & 0x0F;

        advertisement = new AirPodsAdvertisement(
            address,
            modelId,
            model,
            DecodeBattery(leftNibble),
            DecodeBattery(rightNibble),
            DecodeBattery(caseNibble),
            flipped ? (flags & 0x02) != 0 : (flags & 0x01) != 0,
            flipped ? (flags & 0x01) != 0 : (flags & 0x02) != 0,
            (flags & 0x04) != 0,
            (status & 0x40) != 0 && !model.Contains("Max", StringComparison.OrdinalIgnoreCase),
            DecodeWearState(modelId, status),
            DecodeInEarCount(modelId, status),
            data[9],
            status,
            DateTimeOffset.UtcNow);
        return true;
    }

    private static int? DecodeBattery(int nibble) => nibble switch
    {
        >= 0 and <= 9 => nibble * 10,
        >= 10 and <= 14 => 100,
        _ => null
    };

    private static bool DecodeWearState(int modelId, byte status) => modelId switch
    {
        0x2D20 => (status & 0x0A) != 0,
        0x0A20 or 0x1F20 => (status & 0x20) != 0,
        _ => (status & 0x0A) == 0x0A
    };

    private static int DecodeInEarCount(int modelId, byte status) => modelId switch
    {
        0x2D20 => (status & 0x0A) != 0 ? 1 : 0,
        0x0A20 or 0x1F20 => (status & 0x20) != 0 ? 1 : 0,
        _ => ((status & 0x02) != 0 ? 1 : 0) + ((status & 0x08) != 0 ? 1 : 0)
    };
}
