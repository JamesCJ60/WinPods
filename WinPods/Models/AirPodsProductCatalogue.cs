namespace WinPods.Models;

public static class AirPodsProductCatalogue
{
    private const string ProductRoot = "pack://application:,,,/WinPods;component/Assets/Products/";
    private const string FallbackImage = ProductRoot + "airpods-4.png";

    private static readonly IReadOnlyDictionary<int, string> Names = new Dictionary<int, string>
    {
        [0x0220] = "AirPods (1st generation)",
        [0x0F20] = "AirPods (2nd generation)",
        [0x1320] = "AirPods (3rd generation)",
        [0x1920] = "AirPods 4",
        [0x1B20] = "AirPods 4 with ANC",
        [0x0E20] = "AirPods Pro",
        [0x1420] = "AirPods Pro 2",
        [0x2420] = "AirPods Pro 2 (USB-C)",
        [0x2720] = "AirPods Pro 3",
        [0x0A20] = "AirPods Max",
        [0x1F20] = "AirPods Max (USB-C)",
        [0x2D20] = "AirPods Max 2",
        [0x0320] = "Powerbeats 3",
        [0x0520] = "BeatsX",
        [0x0620] = "Beats Solo3",
        [0x0920] = "Beats Studio3",
        [0x0B20] = "Powerbeats Pro",
        [0x0C20] = "Powerbeats",
        [0x0D20] = "Beats Solo Pro",
        [0x1020] = "Beats Flex",
        [0x1120] = "Beats Studio Buds",
        [0x1220] = "Beats Fit Pro",
        [0x1720] = "Beats Studio Buds+"
    };

    public static string GetModelName(int productId, string fallbackName) =>
        Names.GetValueOrDefault(productId, InferFromName(fallbackName));

    public static bool IsOverEar(int productId) =>
        productId is 0x0A20 or 0x1F20 or 0x2D20 or 0x0620 or 0x0920 or 0x0D20;

    public static bool HasColourVariants(int productId) =>
        productId is 0x0A20 or 0x1F20 or 0x2D20 or 0x0520 or 0x0620 or 0x0920
            or 0x0B20 or 0x0D20 or 0x1020 or 0x1120 or 0x1220 or 0x1720;

    public static int InferProductId(string name)
    {
        if (name.Contains("Beats", StringComparison.OrdinalIgnoreCase))
            return InferBeatsProductId(name);
        if (name.Contains("Pro 3", StringComparison.OrdinalIgnoreCase)) return 0x2720;
        if (name.Contains("Pro 2", StringComparison.OrdinalIgnoreCase)) return 0x1420;
        if (name.Contains("Pro", StringComparison.OrdinalIgnoreCase)) return 0x0E20;
        if (name.Contains("Max 2", StringComparison.OrdinalIgnoreCase)) return 0x2D20;
        if (name.Contains("Max", StringComparison.OrdinalIgnoreCase)) return 0x0A20;
        if (name.Contains("AirPods 4", StringComparison.OrdinalIgnoreCase)) return 0x1920;
        if (name.Contains("3rd", StringComparison.OrdinalIgnoreCase)) return 0x1320;
        if (name.Contains("2nd", StringComparison.OrdinalIgnoreCase)) return 0x0F20;
        return 0;
    }

    public static string GetProductImagePath(int productId, byte? deviceColour = null) => productId switch
    {
        0x0220 or 0x0F20 => ProductImage("airpods-2.png"),
        0x1320 => ProductImage("airpods-3.png"),
        0x1920 or 0x1B20 => ProductImage("airpods-4.png"),
        0x2720 => ProductImage("airpods-pro-3.png"),
        0x0E20 or 0x1420 or 0x2420 => ProductImage("airpods-pro.png"),
        0x0A20 => GetOriginalMaxImage(deviceColour),
        0x1F20 or 0x2D20 => GetCurrentMaxImage(deviceColour),
        0x0B20 => ColouredImage("powerbeats-pro", PowerbeatsProColour(deviceColour)),
        0x1220 => ColouredImage("beats-fit-pro", FitProColour(deviceColour)),
        0x1120 => ColouredImage("beats-studio-buds", StudioBudsColour(deviceColour)),
        0x1720 => ColouredImage("beats-studio-buds-plus", StudioBudsPlusColour(deviceColour)),
        0x0620 => ColouredImage("beats-solo3", HeadphoneColour(deviceColour)),
        0x0920 => ColouredImage("beats-studio3", HeadphoneColour(deviceColour)),
        0x0D20 => ColouredImage("beats-solo-pro", HeadphoneColour(deviceColour)),
        0x1020 => ColouredImage("beats-flex", HeadphoneColour(deviceColour)),
        0x0520 => ProductImage("beatsx.png"),
        0x0320 => ProductImage("powerbeats-3.png"),
        0x0C20 => ProductImage("powerbeats.png"),
        _ => FallbackImage
    };

    public static System.Windows.Rect GetProductImageViewbox(int productId) => productId switch
    {
        0x0220 or 0x0F20 => new(0, 0.26, 1, 0.74),
        0x1320 => new(0, 0.30, 1, 0.70),
        0x1920 or 0x1B20 => new(0, 0.30, 1, 0.70),
        0x0E20 or 0x1420 or 0x2420 => new(0, 0.36, 1, 0.64),
        _ => new(0, 0, 1, 1)
    };

    private static int InferBeatsProductId(string name)
    {
        if (name.Contains("Fit Pro", StringComparison.OrdinalIgnoreCase)) return 0x1220;
        if (name.Contains("Studio Buds", StringComparison.OrdinalIgnoreCase))
            return name.Contains("+", StringComparison.Ordinal) ||
                   name.Contains("Plus", StringComparison.OrdinalIgnoreCase) ? 0x1720 : 0x1120;
        if (name.Contains("Powerbeats Pro", StringComparison.OrdinalIgnoreCase)) return 0x0B20;
        if (name.Contains("Powerbeats 3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Powerbeats3", StringComparison.OrdinalIgnoreCase)) return 0x0320;
        if (name.Contains("Powerbeats", StringComparison.OrdinalIgnoreCase)) return 0x0C20;
        if (name.Contains("Solo Pro", StringComparison.OrdinalIgnoreCase)) return 0x0D20;
        if (name.Contains("Solo 3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Solo3", StringComparison.OrdinalIgnoreCase)) return 0x0620;
        if (name.Contains("Studio 3", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Studio3", StringComparison.OrdinalIgnoreCase)) return 0x0920;
        if (name.Contains("Flex", StringComparison.OrdinalIgnoreCase)) return 0x1020;
        if (name.Contains("Beats X", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("BeatsX", StringComparison.OrdinalIgnoreCase)) return 0x0520;
        return 0;
    }

    private static string GetOriginalMaxImage(byte? colour) => ProductImage(colour switch
    {
        0x00 => "airpods-max-silver.png",
        0x02 => "airpods-max-pink.png",
        0x03 => "airpods-max-skyblue.png",
        0x11 => "airpods-max-green.png",
        _ => "airpods-max-spacegray.png"
    });

    private static string GetCurrentMaxImage(byte? colour) => ProductImage(colour switch
    {
        0x00 or 0x06 or 0x07 => "airpods-max-starlight.png",
        0x03 or 0x0A or 0x0B or 0x14 or 0x15 or 0x22 => "airpods-max-blue.png",
        0x0E or 0x10 or 0x23 => "airpods-max-purple.png",
        0x02 or 0x0C or 0x21 => "airpods-max-orange.png",
        _ => "airpods-max-midnight.png"
    });

    private static string? FitProColour(byte? colour) => colour switch
    {
        0x01 => "white",
        0x02 => "purple",
        0x03 => "grey",
        0x04 => "blue",
        0x05 => "pink",
        0x06 => "yellow",
        0x00 => "black",
        _ => null
    };

    private static string? StudioBudsColour(byte? colour) => colour switch
    {
        0x01 => "white",
        0x02 => "red",
        0x03 => "grey",
        0x04 => "blue",
        0x05 => "transparent",
        0x00 => "black",
        _ => null
    };

    private static string? StudioBudsPlusColour(byte? colour) => colour switch
    {
        0x01 => "ivory",
        0x02 => "transparent",
        0x03 => "silver",
        0x00 => "black",
        _ => null
    };

    private static string? PowerbeatsProColour(byte? colour) => colour switch
    {
        0x01 => "ivory",
        0x02 => "navy",
        0x03 => "moss",
        0x04 => "cloud",
        0x00 => "black",
        _ => null
    };

    private static string? HeadphoneColour(byte? colour) => colour switch
    {
        0x01 => "grey",
        0x02 => "red",
        0x03 => "blue",
        0x00 => "black",
        _ => null
    };

    private static string ColouredImage(string prefix, string? colour) =>
        colour is not null && ResourceExists($"{prefix}-{colour}.png")
            ? ProductImage($"{prefix}-{colour}.png")
            : ProductImage($"{prefix}.png");

    private static string ProductImage(string fileName) =>
        ResourceExists(fileName) ? ProductRoot + fileName : FallbackImage;

    private static bool ResourceExists(string fileName)
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(ProductRoot + fileName));
            if (info is null) return false;
            info.Stream.Dispose();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string InferFromName(string name)
    {
        if (name.Contains("Powerbeats", StringComparison.OrdinalIgnoreCase)) return "Powerbeats";
        if (name.Contains("Beats", StringComparison.OrdinalIgnoreCase)) return "Beats";
        if (name.Contains("Max", StringComparison.OrdinalIgnoreCase)) return "AirPods Max";
        if (name.Contains("Pro", StringComparison.OrdinalIgnoreCase)) return "AirPods Pro";
        return "AirPods";
    }
}
