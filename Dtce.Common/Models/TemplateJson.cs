namespace Dtce.Common.Models;

public class TemplateJson
{
    public VisualTheme VisualTheme { get; set; } = new();
    public SectionHierarchy SectionHierarchy { get; set; } = new();
    public List<LogoAsset> LogoMap { get; set; } = new();
}

public class VisualTheme
{
    public List<ColorDefinition> ColorPalette { get; set; } = new();
    public Dictionary<string, FontDefinition> FontMap { get; set; } = new();
    public LayoutRules LayoutRules { get; set; } = new();
}

public class ColorDefinition
{
    public string Name { get; set; } = string.Empty; // primary, secondary, accent
    public string HexCode { get; set; } = string.Empty;
}

public class FontDefinition
{
    public string Family { get; set; } = string.Empty;
    public double Size { get; set; }
    public string Weight { get; set; } = string.Empty; // normal, bold, etc.
    public string Color { get; set; } = string.Empty; // hex code
}

public class LayoutRules
{
    public double PageWidth { get; set; } // in millimeters
    public double PageHeight { get; set; } // in millimeters
    public string Orientation { get; set; } = "portrait"; // portrait, landscape
    public MarginDefinition Margins { get; set; } = new();
}

public class MarginDefinition
{
    public double Top { get; set; }
    public double Bottom { get; set; }
    public double Left { get; set; }
    public double Right { get; set; }
}

public class SectionHierarchy
{
    public List<Section> Sections { get; set; } = new();
}

public class Section
{
    public string SectionTitle { get; set; } = string.Empty;
    public string PlaceholderId { get; set; } = string.Empty;
    public List<Section> SubSections { get; set; } = new();
}

public class LogoAsset
{
    public string AssetId { get; set; } = string.Empty;
    public string AssetType { get; set; } = string.Empty; // logo, watermark, image
    public BoundingBox BoundingBox { get; set; } = new();
    public string SecureUrl { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
}

public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int PageNumber { get; set; }
}

