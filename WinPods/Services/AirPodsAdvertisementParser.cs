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
        var (leftBud, rightBud) = DecodeBudLocations(modelId, model, status);

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
            leftBud,
            rightBud,
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

    // Earbud status byte. Each bud takes turns sending advertisements, and the bits describe:
    //   0x20 the sending bud is the left one     0x40 the sending bud is in the case
    //   0x10 one bud is in the case              0x04 both buds are in the case
    //   0x02 the primary bud is in an ear        0x08 the other bud is in an ear
    // The sender is normally the primary bud, but a bud in the case hands that role to its
    // partner, so the in-ear bits swap sides whenever the sender is in the case. Verified against
    // live AirPods Pro 3 captures with the left bud in the case and the right one worn: the right
    // bud sends 0x13 and the left bud sends 0x73, and both must decode to (InCase, InEar).
    internal static (BudLocation Left, BudLocation Right) DecodeBudLocations(int modelId, string model, byte status)
    {
        if (AirPodsProductCatalogue.IsOverEar(modelId) || model.Contains("Max", StringComparison.OrdinalIgnoreCase))
            return (BudLocation.Unknown, BudLocation.Unknown);

        var senderIsLeft = (status & 0x20) != 0;
        var senderInCase = (status & 0x40) != 0;
        var bothInCase = (status & 0x04) != 0;
        var oneInCase = (status & 0x10) != 0;

        var primaryIsLeft = senderIsLeft ^ senderInCase;
        var leftInEar = (status & (primaryIsLeft ? 0x02 : 0x08)) != 0;
        var rightInEar = (status & (primaryIsLeft ? 0x08 : 0x02)) != 0;

        var senderBudInCase = bothInCase || senderInCase;
        var otherBudInCase = bothInCase || (oneInCase && !senderInCase);
        var leftInCase = senderIsLeft ? senderBudInCase : otherBudInCase;
        var rightInCase = senderIsLeft ? otherBudInCase : senderBudInCase;

        return (Locate(leftInEar, leftInCase), Locate(rightInEar, rightInCase));
    }

    // The ear sensor is the most reliable signal, so it wins if the flags ever disagree.
    private static BudLocation Locate(bool inEar, bool inCase) =>
        inEar ? BudLocation.InEar : inCase ? BudLocation.InCase : BudLocation.OutOfEar;

    private static int DecodeInEarCount(int modelId, byte status) => modelId switch
    {
        0x2D20 => (status & 0x0A) != 0 ? 1 : 0,
        0x0A20 or 0x1F20 => (status & 0x20) != 0 ? 1 : 0,
        _ => ((status & 0x02) != 0 ? 1 : 0) + ((status & 0x08) != 0 ? 1 : 0)
    };
}
