using System.Globalization;
using System.Text;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace Dtce.ParsingEngine.Rendering;

public class TemplateDocumentRenderer
{
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<TemplateDocumentRenderer> _logger;

    public TemplateDocumentRenderer(IObjectStorage objectStorage, ILogger<TemplateDocumentRenderer> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
    }

    public async Task<byte[]> RenderAsync(
        TemplateJson template,
        ContextJson context,
        TemplateRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        options ??= new TemplateRenderOptions();

        // Use temporary file approach for more reliable document creation
        var tempPath = Path.Combine(Path.GetTempPath(), $"dtce_{Guid.NewGuid():N}.docx");
        try
        {
            using (var document = WordprocessingDocument.Create(tempPath, WordprocessingDocumentType.Document))
            {
                var mainPart = document.AddMainDocumentPart();
                
                // Create document with body first
                mainPart.Document = new Document();
                mainPart.Document.Body = new Body();

                // Apply styles BEFORE adding content (ensures styles exist when referenced)
                ApplyDefaultStyles(mainPart, template.VisualTheme);
                
                // Apply page setup AFTER styles but BEFORE content
                ApplyPageSetup(mainPart, template.VisualTheme.LayoutRules);

                // Insert logos
                await InsertLogosAsync(mainPart, template, options, cancellationToken);
                
                // Append sections
                AppendSections(mainPart.Document.Body, template.SectionHierarchy.Sections, context, template.VisualTheme, options);

                // Ensure body is not empty (Word requires at least one paragraph)
                if (!mainPart.Document.Body.Elements().Any())
                {
                    var emptyParagraph = new Paragraph(new Run(new Text(" ")));
                    emptyParagraph.ParagraphProperties = new ParagraphProperties
                    {
                        ParagraphStyleId = new ParagraphStyleId { Val = "Normal" }
                    };
                    mainPart.Document.Body.Append(emptyParagraph);
                }

                // Ensure SectionProperties is the last child of Body (Word requirement)
                var sectionProps = mainPart.Document.Body.Elements<SectionProperties>().ToList();
                if (sectionProps.Any())
                {
                    foreach (var sp in sectionProps)
                    {
                        mainPart.Document.Body.RemoveChild(sp);
                    }
                    // Re-add SectionProperties as the last element
                    var lastSectionProps = sectionProps.Last();
                    mainPart.Document.Body.Append(lastSectionProps);
                }

                // Save document - this will save all parts including styles
                mainPart.Document.Save();
            }
            
            // Verify the file was created and is readable
            if (!File.Exists(tempPath))
            {
                throw new InvalidOperationException("Failed to create document file");
            }
            
            var fileInfo = new FileInfo(tempPath);
            if (fileInfo.Length == 0)
            {
                throw new InvalidOperationException("Created document file is empty");
            }

            // Read the file back into memory
            return await File.ReadAllBytesAsync(tempPath, cancellationToken);
        }
        finally
        {
            // Clean up temporary file
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private void ApplyPageSetup(MainDocumentPart mainPart, LayoutRules layoutRules)
    {
        const double MillimetersToTwips = 56.69291339;

        var sectionProperties = new SectionProperties();

        var pageSize = new PageSize
        {
            Width = (UInt32Value)(layoutRules.PageWidth * MillimetersToTwips),
            Height = (UInt32Value)(layoutRules.PageHeight * MillimetersToTwips)
        };

        if (layoutRules.Orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase))
        {
            pageSize.Orient = PageOrientationValues.Landscape;
        }

        var pageMargin = new PageMargin
        {
            Top = (Int32Value)(layoutRules.Margins.Top * MillimetersToTwips),
            Bottom = (Int32Value)(layoutRules.Margins.Bottom * MillimetersToTwips),
            Left = (UInt32Value)(layoutRules.Margins.Left * MillimetersToTwips),
            Right = (UInt32Value)(layoutRules.Margins.Right * MillimetersToTwips)
        };

        sectionProperties.Append(pageSize);
        sectionProperties.Append(pageMargin);

        mainPart.Document.Body ??= new Body();
        if (mainPart.Document.Body.ChildElements.OfType<SectionProperties>().Any())
        {
            mainPart.Document.Body.RemoveAllChildren<SectionProperties>();
        }
        mainPart.Document.Body.Append(sectionProperties);
    }

    private void ApplyDefaultStyles(MainDocumentPart mainPart, VisualTheme theme)
    {
        var stylesPart = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();
        
        if (stylesPart.Styles == null)
        {
            stylesPart.Styles = new Styles();
        }

        // Ensure DefaultParagraphFont is present (required by Word)
        if (!stylesPart.Styles.Elements<DocDefaults>().Any())
        {
            var docDefaults = new DocDefaults();
            var defaultRunProps = new RunPropertiesDefault();
            var runProps = new RunProperties();
            runProps.Append(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
            runProps.Append(new FontSize { Val = "22" }); // 11pt = 22 half-points
            runProps.Append(new FontSizeComplexScript { Val = "22" });
            defaultRunProps.Append(runProps);
            docDefaults.Append(defaultRunProps);
            stylesPart.Styles.Append(docDefaults);
        }

        var normalStyle = stylesPart.Styles.Elements<Style>().FirstOrDefault(s => s.StyleId == "Normal");
        if (normalStyle == null)
        {
            normalStyle = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true,
                StyleName = new StyleName { Val = "Normal" },
                UIPriority = new UIPriority { Val = 99 },
                PrimaryStyle = new PrimaryStyle()
            };
            stylesPart.Styles.Append(normalStyle);
        }

        var bodyFont = GetFontDefinition(theme, "Normal") ??
                       new FontDefinition { Family = "Calibri", Size = 11, Weight = "normal", Color = "#000000" };

        normalStyle.StyleRunProperties = CreateStyleRunProperties(bodyFont);

        // Add heading styles
        for (var level = 1; level <= 6; level++)
        {
            var styleId = $"Heading{level}";
            var headingStyle = stylesPart.Styles.Elements<Style>().FirstOrDefault(s => s.StyleId == styleId);
            if (headingStyle == null)
            {
                headingStyle = new Style
                {
                    Type = StyleValues.Paragraph,
                    StyleId = styleId,
                    StyleName = new StyleName { Val = styleId },
                    BasedOn = new BasedOn { Val = "Normal" },
                    UIPriority = new UIPriority { Val = 10 - level }
                };
                stylesPart.Styles.Append(headingStyle);
            }

            var headingFont = GetFontDefinition(theme, $"heading {level}") ??
                              GetFontDefinition(theme, "Title") ??
                              bodyFont;

            headingStyle.StyleRunProperties = CreateStyleRunProperties(headingFont, boldOverride: true);
        }
    }

    private async Task InsertLogosAsync(
        MainDocumentPart mainPart,
        TemplateJson template,
        TemplateRenderOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.IncludeLogos || template.LogoMap.Count == 0)
        {
            return;
        }

        foreach (var logo in template.LogoMap.OrderBy(l => l.AssetId))
        {
            try
            {
                var imageBytes = await ResolveLogoAsync(logo, options, cancellationToken);
                if (imageBytes == null)
                {
                    continue;
                }

                InsertImage(mainPart, imageBytes, logo);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to render logo {AssetId}", logo.AssetId);
            }
        }
    }

    private async Task<byte[]?> ResolveLogoAsync(
        LogoAsset logo,
        TemplateRenderOptions options,
        CancellationToken cancellationToken)
    {
        if (options.LogoOverrides.TryGetValue(logo.AssetId, out var overrideBytes))
        {
            return overrideBytes;
        }

        if (!options.IncludeTemplateLogosFromStorage || string.IsNullOrWhiteSpace(logo.StorageKey))
        {
            return null;
        }

        await using var stream = await _objectStorage.DownloadFileAsync(logo.StorageKey, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private void InsertImage(MainDocumentPart mainPart, byte[] imageBytes, LogoAsset logo)
    {
        // Detect image type from bytes and add image part
        ImagePart imagePart;
        if (imageBytes.Length >= 4)
        {
            // PNG signature
            if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            {
                imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Png);
            }
            // JPEG signature
            else if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Jpeg);
            }
            // GIF signature
            else if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46)
            {
                imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Gif);
            }
            // BMP signature
            else if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
            {
                imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Bmp);
            }
            else
            {
                imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Png); // Default fallback
            }
        }
        else
        {
            imagePart = mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Png); // Default fallback
        }
        
        using (var ms = new MemoryStream(imageBytes))
        {
            imagePart.FeedData(ms);
        }

        var relationshipId = mainPart.GetIdOfPart(imagePart);
        var drawing = CreateDrawing(relationshipId, logo);
        var paragraph = new Paragraph(new Run(drawing))
        {
            ParagraphProperties = new ParagraphProperties
            {
                Justification = new Justification { Val = JustificationValues.Center },
                ParagraphStyleId = new ParagraphStyleId { Val = "Normal" }
            }
        };

        if (mainPart.Document.Body == null)
        {
            mainPart.Document.Body = new Body();
        }
        mainPart.Document.Body.Append(paragraph);
    }


    private static Drawing CreateDrawing(string relationshipId, LogoAsset logo)
    {
        const double DefaultWidth = 180;
        const double DefaultHeight = 120;

        var width = logo.BoundingBox.Width > 0 ? logo.BoundingBox.Width : DefaultWidth;
        var height = logo.BoundingBox.Height > 0 ? logo.BoundingBox.Height : DefaultHeight;

        var extentCx = ToEmu(width);
        var extentCy = ToEmu(height);

        var inline = new DW.Inline(
            new DW.Extent { Cx = extentCx, Cy = extentCy },
            new DW.EffectExtent
            {
                LeftEdge = 0L,
                TopEdge = 0L,
                RightEdge = 0L,
                BottomEdge = 0L
            },
            new DW.DocProperties { Id = (UInt32Value)1U, Name = logo.AssetId },
            new DW.NonVisualGraphicFrameDrawingProperties(
                new A.GraphicFrameLocks { NoChangeAspect = true }),
            new A.Graphic(
                new A.GraphicData(
                    new A.Pictures.Picture(
                        new A.Pictures.NonVisualPictureProperties(
                            new A.Pictures.NonVisualDrawingProperties
                            {
                                Id = (UInt32Value)1U,
                                Name = logo.AssetId
                            },
                            new A.Pictures.NonVisualPictureDrawingProperties()),
                        new A.Pictures.BlipFill(
                            new A.Blip
                            {
                                Embed = relationshipId,
                                CompressionState = A.BlipCompressionValues.Print
                            },
                            new A.Stretch(new A.FillRectangle())),
                        new A.Pictures.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = extentCx, Cy = extentCy }),
                            new A.PresetGeometry
                            {
                                Preset = A.ShapeTypeValues.Rectangle,
                                AdjustValueList = new A.AdjustValueList()
                            })))));

        inline.DistanceFromTop = 0U;
        inline.DistanceFromBottom = 0U;
        inline.DistanceFromLeft = 0U;
        inline.DistanceFromRight = 0U;

        return new Drawing(inline);
    }

    private void AppendSections(
        Body body,
        IReadOnlyCollection<Section> sections,
        ContextJson context,
        VisualTheme theme,
        TemplateRenderOptions options,
        int level = 1)
    {
        foreach (var section in sections)
        {
            AppendHeading(body, section.SectionTitle, theme, level);

            if (TryGetContent(section.PlaceholderId, context, options, out var contentText))
            {
                AppendContent(body, contentText, theme);
            }
            else if (options.EmitPlaceholderForMissingContent)
            {
                AppendContent(body, $"{{{{{section.PlaceholderId}}}}}", theme);
            }

            if (section.SubSections?.Count > 0)
            {
                AppendSections(body, section.SubSections, context, theme, options, Math.Min(level + 1, 6));
            }
        }
    }

    private void AppendHeading(Body body, string text, VisualTheme theme, int level)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var headingFont = GetFontDefinition(theme, $"heading {level}") ??
                          GetFontDefinition(theme, "Title") ??
                          GetFontDefinition(theme, "Normal") ??
                          new FontDefinition { Family = "Calibri", Size = 14, Weight = "bold", Color = "#000000" };

        var sanitizedText = SanitizeXmlText(text);
        var textElement = new Text(sanitizedText);
        textElement.Space = SpaceProcessingModeValues.Preserve;
        
        var run = new Run(textElement);
        run.RunProperties = CreateRunProperties(headingFont, boldOverride: true);

        var paragraph = new Paragraph(run)
        {
            ParagraphProperties = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = $"Heading{Math.Clamp(level, 1, 6)}" }
            }
        };

        body.Append(paragraph);
    }

    private void AppendContent(Body body, string content, VisualTheme theme)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var bodyFont = GetFontDefinition(theme, "Normal") ??
                       new FontDefinition { Family = "Calibri", Size = 11, Weight = "normal", Color = "#000000" };

        var lines = SplitContentIntoParagraphs(content);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Handle text with proper XML escaping - OpenXML handles this automatically
            // But we need to ensure no invalid XML characters
            var sanitizedLine = SanitizeXmlText(line);
            var text = new Text(sanitizedLine);
            text.Space = SpaceProcessingModeValues.Preserve;
            
            var run = new Run(text)
            {
                RunProperties = CreateRunProperties(bodyFont)
            };
            
            var paragraph = new Paragraph(run)
            {
                ParagraphProperties = new ParagraphProperties
                {
                    ParagraphStyleId = new ParagraphStyleId { Val = "Normal" }
                }
            };
            
            body.Append(paragraph);
        }
    }

    private static IEnumerable<string> SplitContentIntoParagraphs(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static StyleRunProperties CreateStyleRunProperties(FontDefinition font, bool boldOverride = false)
    {
        var styleProps = new StyleRunProperties();

        // Order matters in OpenXML! Follow the schema order:
        // 1. RunFonts
        // 2. Bold/Italic/Underline
        // 3. FontSize and FontSizeComplexScript
        // 4. Color (must come after FontSize)

        if (!string.IsNullOrWhiteSpace(font.Family))
        {
            styleProps.Append(new RunFonts { Ascii = font.Family, HighAnsi = font.Family });
        }

        var isBold = boldOverride || string.Equals(font.Weight, "bold", StringComparison.OrdinalIgnoreCase);
        if (isBold)
        {
            styleProps.Append(new Bold());
        }

        if (font.Size > 0)
        {
            var size = (font.Size * 2).ToString("F0", CultureInfo.InvariantCulture);
            styleProps.Append(new FontSize { Val = size });
            styleProps.Append(new FontSizeComplexScript { Val = size });
        }

        if (!string.IsNullOrWhiteSpace(font.Color))
        {
            styleProps.Append(new Color { Val = NormalizeColor(font.Color) });
        }

        return styleProps;
    }

    private static RunProperties CreateRunProperties(FontDefinition font, bool boldOverride = false)
    {
        var runProps = new RunProperties();

        // Order matters in OpenXML! Follow the schema order:
        // 1. RunFonts
        // 2. Bold/Italic/Underline
        // 3. FontSize
        // 4. Color

        if (!string.IsNullOrWhiteSpace(font.Family))
        {
            runProps.Append(new RunFonts { Ascii = font.Family, HighAnsi = font.Family });
        }

        var isBold = boldOverride || string.Equals(font.Weight, "bold", StringComparison.OrdinalIgnoreCase);
        if (isBold)
        {
            runProps.Append(new Bold());
        }

        if (font.Size > 0)
        {
            var size = (font.Size * 2).ToString("F0", CultureInfo.InvariantCulture);
            runProps.Append(new FontSize { Val = size });
        }

        if (!string.IsNullOrWhiteSpace(font.Color))
        {
            runProps.Append(new Color { Val = NormalizeColor(font.Color) });
        }

        return runProps;
    }

    private static string NormalizeColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return "000000";
        }

        var trimmed = color.Trim('#');
        return trimmed.Length switch
        {
            3 => new string(trimmed.SelectMany(c => new[] { c, c }).ToArray()),
            6 => trimmed.ToUpperInvariant(),
            _ => "000000"
        };
    }

    private static string DetectImageContentType(byte[] imageBytes)
    {
        if (imageBytes.Length >= 4)
        {
            // PNG signature
            if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            {
                return "image/png";
            }

            // JPEG signature
            if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8)
            {
                return "image/jpeg";
            }

            // GIF signature
            if (imageBytes[0] == 0x47 && imageBytes[1] == 0x49 && imageBytes[2] == 0x46)
            {
                return "image/gif";
            }

            // BMP signature
            if (imageBytes[0] == 0x42 && imageBytes[1] == 0x4D)
            {
                return "image/bmp";
            }
        }

        return "image/png"; // Default fallback
    }

    private static long ToEmu(double pixelValue)
    {
        // Convert assuming 96 DPI -> 1 inch = 914400 EMUs
        return (long)(pixelValue / 96.0 * 914400.0);
    }

    private bool TryGetContent(string placeholderId, ContextJson context, TemplateRenderOptions options, out string content)
    {
        if (options.ContentOverrides.TryGetValue(placeholderId, out var overrideText))
        {
            content = overrideText;
            return true;
        }

        var block = context.ContentBlocks.FirstOrDefault(b =>
            string.Equals(b.PlaceholderId, placeholderId, StringComparison.OrdinalIgnoreCase));

        if (block != null)
        {
            content = block.SectionSampleText;
            return true;
        }

        content = string.Empty;
        return false;
    }

    private static FontDefinition? GetFontDefinition(VisualTheme theme, string key)
    {
        if (theme.FontMap.TryGetValue(key, out var font))
        {
            return font;
        }

        return theme.FontMap
            .Where(kvp => string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Value)
            .FirstOrDefault();
    }

    private static string SanitizeXmlText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Remove invalid XML characters (control characters except tab, newline, carriage return)
        var sanitized = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
            {
                continue; // Skip invalid control characters
            }
            sanitized.Append(ch);
        }

        return sanitized.ToString();
    }

}

public class TemplateRenderOptions
{
    public bool IncludeLogos { get; set; } = true;
    public bool IncludeTemplateLogosFromStorage { get; set; } = true;
    public bool EmitPlaceholderForMissingContent { get; set; } = true;
    public Dictionary<string, string> ContentOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, byte[]> LogoOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
}

