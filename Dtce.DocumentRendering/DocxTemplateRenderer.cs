using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using Microsoft.Extensions.Logging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Dtce.DocumentRendering;

public sealed class DocxTemplateRenderer
{
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<DocxTemplateRenderer> _logger;

    public DocxTemplateRenderer(IObjectStorage objectStorage, ILogger<DocxTemplateRenderer> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
    }

    public async Task<string> CreateTemplateDocumentAsync(ParseResult parseResult, string templatePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            throw new ArgumentException("Template path must be provided.", nameof(templatePath));
        }

        var directory = Path.GetDirectoryName(templatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var document = WordprocessingDocument.Create(templatePath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());

        // Apply styles BEFORE adding content (ensures styles exist when referenced)
        ApplyDefaultStyles(mainPart, parseResult.TemplateJson.VisualTheme);

        // Apply page setup AFTER styles but BEFORE content
        ApplyPageSettings(mainPart, parseResult.TemplateJson.VisualTheme.LayoutRules);

        if (parseResult.TemplateJson.LogoMap.Any())
        {
            await InsertLogosAsync(mainPart, parseResult.TemplateJson.LogoMap, cancellationToken);
        }

        var body = mainPart.Document.Body ?? throw new InvalidOperationException("Document body not created.");
        var contentLookup = parseResult.ContentSections.ToDictionary(section => section.PlaceholderId, section => section);

        foreach (var section in parseResult.TemplateJson.SectionHierarchy.Sections)
        {
            AppendSection(body, section, parseResult.TemplateJson, contentLookup, depth: 0);
        }

        foreach (var remaining in contentLookup.Values)
        {
            AppendContentPlaceholder(body, remaining, GetBodyFont(parseResult.TemplateJson.VisualTheme));
        }

        // Ensure body is not empty (Word requires at least one paragraph)
        if (!body.Elements().Any())
        {
            var emptyParagraph = new Paragraph(new Run(new Text(" ")));
            emptyParagraph.ParagraphProperties = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = "Normal" }
            };
            body.Append(emptyParagraph);
        }

        // Ensure SectionProperties is the last child of Body (Word requirement)
        var sectionProps = body.Elements<SectionProperties>().ToList();
        if (sectionProps.Any())
        {
            foreach (var sp in sectionProps)
            {
                body.RemoveChild(sp);
            }
            // Re-add SectionProperties as the last element
            var lastSectionProps = sectionProps.Last();
            body.Append(lastSectionProps);
        }

        mainPart.Document.Save();
        _logger.LogInformation("Template document created at {TemplatePath}", templatePath);
        return templatePath;
    }

    public async Task<string> RenderDocumentAsync(
        string templatePath,
        TemplateJson template,
        DocumentRenderRequest request,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(templatePath);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(templatePath, outputPath, overwrite: true);

        using var document = WordprocessingDocument.Open(outputPath, true);
        var mainPart = document.MainDocumentPart ?? throw new InvalidOperationException("Template is missing the main document part.");
        var body = mainPart.Document.Body ?? throw new InvalidOperationException("Template is missing a document body.");

        ReplacePlaceholders(body, template.VisualTheme, request.SectionContent);
        await ReplaceLogosAsync(mainPart, request.LogoReplacements, cancellationToken);

        // Ensure body is not empty (Word requires at least one paragraph)
        if (!body.Elements().Any())
        {
            var emptyParagraph = new Paragraph(new Run(new Text(" ")));
            emptyParagraph.ParagraphProperties = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = "Normal" }
            };
            body.Append(emptyParagraph);
        }

        // Ensure SectionProperties is the last child of Body (Word requirement)
        var sectionProps = body.Elements<SectionProperties>().ToList();
        if (sectionProps.Any())
        {
            foreach (var sp in sectionProps)
            {
                body.RemoveChild(sp);
            }
            // Re-add SectionProperties as the last element
            var lastSectionProps = sectionProps.Last();
            body.Append(lastSectionProps);
        }

        mainPart.Document.Save();

        _logger.LogInformation("Rendered document created at {OutputPath}", outputPath);
        return outputPath;
    }

    private static void ApplyPageSettings(MainDocumentPart mainPart, LayoutRules layoutRules)
    {
        var sectionProps = new SectionProperties();

        if (layoutRules.PageWidth > 0 && layoutRules.PageHeight > 0)
        {
            var pageSize = new PageSize
            {
                Width = new UInt32Value((uint)MillimetersToTwips(layoutRules.PageWidth)),
                Height = new UInt32Value((uint)MillimetersToTwips(layoutRules.PageHeight))
            };

            if (string.Equals(layoutRules.Orientation, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                pageSize.Orient = PageOrientationValues.Landscape;
            }

            sectionProps.Append(pageSize);
        }

        sectionProps.Append(new PageMargin
        {
            Top = new Int32Value((int)MillimetersToTwips(layoutRules.Margins.Top)),
            Bottom = new Int32Value((int)MillimetersToTwips(layoutRules.Margins.Bottom)),
            Left = new UInt32Value((uint)Math.Max(0, MillimetersToTwips(layoutRules.Margins.Left))),
            Right = new UInt32Value((uint)Math.Max(0, MillimetersToTwips(layoutRules.Margins.Right)))
        });

        mainPart.Document.Body ??= new Body();
        // Remove any existing SectionProperties before adding new one
        if (mainPart.Document.Body.ChildElements.OfType<SectionProperties>().Any())
        {
            mainPart.Document.Body.RemoveAllChildren<SectionProperties>();
        }
        mainPart.Document.Body.Append(sectionProps);
    }

    private async Task InsertLogosAsync(MainDocumentPart mainPart, IEnumerable<LogoAsset> logos, CancellationToken cancellationToken)
    {
        foreach (var logo in logos)
        {
            if (string.IsNullOrWhiteSpace(logo.StorageKey))
            {
                continue;
            }

            try
            {
                await using var stream = await _objectStorage.DownloadFileAsync(logo.StorageKey, cancellationToken);
                using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                memory.Position = 0;

                var extension = Path.GetExtension(logo.StorageKey).ToLowerInvariant();
                var imagePart = extension switch
                {
                    ".png" => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Png),
                    ".gif" => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Gif),
                    ".bmp" => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Bmp),
                    ".tiff" => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Tiff),
                    ".svg" => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Svg),
                    ".jpeg" or ".jpg" => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Jpeg),
                    _ => mainPart.AddImagePart(DocumentFormat.OpenXml.Packaging.ImagePartType.Png)
                };
                memory.Position = 0;
                imagePart.FeedData(memory);
                var relationshipId = mainPart.GetIdOfPart(imagePart);

                var paragraph = new Paragraph(new Run(CreateDrawing(logo, relationshipId)));
                mainPart.Document.Body?.AppendChild(paragraph);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to embed logo {AssetId} from storage key {StorageKey}", logo.AssetId, logo.StorageKey);
            }
        }
    }

    private void AppendSection(
        Body body,
        Section section,
        TemplateJson template,
        IDictionary<string, ContentSection> contentLookup,
        int depth)
    {
        var font = GetHeadingFont(template.VisualTheme, depth);
        var paragraph = CreateParagraph(section.SectionTitle, font, ShouldBold(font), spacingAfter: 100);
        body.AppendChild(paragraph);

        if (contentLookup.Remove(section.PlaceholderId, out var sectionContent))
        {
            AppendContentPlaceholder(body, sectionContent, GetBodyFont(template.VisualTheme));
        }

        foreach (var subSection in section.SubSections)
        {
            AppendSection(body, subSection, template, contentLookup, depth + 1);
        }
    }

    private void AppendContentPlaceholder(Body body, ContentSection content, FontDefinition font)
    {
        var placeholderText = GetPlaceholderToken(content.PlaceholderId);
        var placeholderParagraph = CreateParagraph(placeholderText, font, isBold: false, spacingAfter: 80, isPlaceholder: true);
        body.AppendChild(placeholderParagraph);
    }

    private static Paragraph CreateParagraph(
        string text,
        FontDefinition font,
        bool isBold,
        int spacingAfter = 200,
        bool isPlaceholder = false)
    {
        var runProperties = new RunProperties();
        if (!string.IsNullOrWhiteSpace(font.Family))
        {
            runProperties.Append(new RunFonts { Ascii = font.Family, HighAnsi = font.Family, EastAsia = font.Family });
        }

        if (font.Size > 0)
        {
            var sizeValue = ((int)Math.Round(font.Size * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            runProperties.Append(new FontSize { Val = sizeValue });
        }

        if (!string.IsNullOrWhiteSpace(font.Color))
        {
            runProperties.Append(new Color { Val = NormalizeColorValue(font.Color) });
        }

        if (isBold)
        {
            runProperties.Append(new Bold());
        }

        if (isPlaceholder)
        {
            runProperties.Append(new Italic());
        }

        var run = new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var paragraph = new Paragraph(run);
        var paragraphProperties = new ParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = spacingAfter.ToString(CultureInfo.InvariantCulture) }
        };

        paragraph.PrependChild(paragraphProperties);
        return paragraph;
    }

    private static bool ShouldBold(FontDefinition fontDefinition) =>
        string.Equals(fontDefinition.Weight, "bold", StringComparison.OrdinalIgnoreCase);

    private static FontDefinition GetHeadingFont(VisualTheme theme, int depth)
    {
        var key = depth switch
        {
            0 => "heading 1",
            1 => "heading 2",
            2 => "heading 3",
            3 => "heading 4",
            _ => "heading 5"
        };

        return GetFont(theme, key, DefaultHeadingFont(depth));
    }

    private static FontDefinition GetBodyFont(VisualTheme theme) =>
        GetFont(theme, "normal", new FontDefinition
        {
            Family = "Calibri",
            Size = 11,
            Weight = "normal",
            Color = "#000000"
        });

    private static FontDefinition GetFont(VisualTheme theme, string key, FontDefinition fallback)
    {
        if (theme.FontMap.Count == 0)
        {
            return fallback;
        }

        var match = theme.FontMap.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
        return match.Equals(default(KeyValuePair<string, FontDefinition>)) ? fallback : match.Value;
    }

    private static FontDefinition DefaultHeadingFont(int depth) => new()
    {
        Family = "Calibri",
        Size = depth switch
        {
            0 => 18,
            1 => 16,
            2 => 14,
            _ => 12
        },
        Weight = "bold",
        Color = "#000000"
    };

    private void ReplacePlaceholders(Body body, VisualTheme theme, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        var paragraphs = body.Descendants<Paragraph>().ToList();
        var bodyFont = GetBodyFont(theme);

        foreach (var paragraph in paragraphs)
        {
            var rawText = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
            if (!TryExtractPlaceholder(rawText, out var placeholderId))
            {
                continue;
            }

            if (!replacements.TryGetValue(placeholderId, out var replacement))
            {
                paragraph.Remove();
                continue;
            }

            var newParagraphs = BuildContentParagraphs(replacement, bodyFont);
            foreach (var newParagraph in newParagraphs)
            {
                paragraph.InsertBeforeSelf(newParagraph);
            }

            paragraph.Remove();
        }
    }

    private static bool TryExtractPlaceholder(string text, out string placeholderId)
    {
        placeholderId = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = PlaceholderRegex.Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        placeholderId = match.Groups["id"].Value;
        return true;
    }

    private static IEnumerable<Paragraph> BuildContentParagraphs(string content, FontDefinition font)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        // Preserve original line structure and detail level
        // Split by newlines but preserve empty lines to maintain spacing
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Skip completely empty lines but preserve structure
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                // Add minimal spacing paragraph
                yield return CreateParagraph(" ", font, isBold: false, spacingAfter: 40);
                continue;
            }

            var isBullet = trimmed.StartsWith("- ", StringComparison.Ordinal) ||
                           trimmed.StartsWith("* ", StringComparison.Ordinal) ||
                           trimmed.StartsWith("•", StringComparison.Ordinal);

            var textValue = isBullet ? $"• {trimmed.TrimStart('-', '*', '•', ' ')}" : trimmed;
            
            // Preserve original spacing - use smaller spacing for bullet points
            var spacing = isBullet ? 60 : 80;
            yield return CreateParagraph(textValue, font, ShouldBold(font), spacingAfter: spacing);
        }
    }

    private async Task ReplaceLogosAsync(MainDocumentPart mainPart, IReadOnlyCollection<LogoReplacement> replacements, CancellationToken cancellationToken)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        var replacementLookup = replacements.ToDictionary(r => r.AssetId, r => r);
        var drawings = mainPart.Document.Body?.Descendants<Drawing>().ToList() ?? new List<Drawing>();

        foreach (var drawing in drawings)
        {
            var docProperties = drawing.Descendants<DW.DocProperties>().FirstOrDefault();
            var assetKey = docProperties?.Title ?? docProperties?.Name;
            if (string.IsNullOrWhiteSpace(assetKey))
            {
                continue;
            }

            if (!replacementLookup.TryGetValue(assetKey!, out var replacement))
            {
                continue;
            }

            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip?.Embed == null || string.IsNullOrWhiteSpace(blip.Embed.Value))
            {
                continue;
            }

            var relationshipId = blip.Embed.Value;
            if (!mainPart.Parts.Any(p => p.RelationshipId == relationshipId))
            {
                continue;
            }

            var imagePart = (ImagePart)mainPart.GetPartById(relationshipId);
            await using var stream = new MemoryStream(replacement.Content);
            stream.Position = 0;
            imagePart.FeedData(stream);
        }
    }

    private static Drawing CreateDrawing(LogoAsset logo, string relationshipId)
    {
        var width = logo.BoundingBox.Width > 0 ? Convert.ToInt64(logo.BoundingBox.Width * EmusPerPixel) : DefaultImageWidth;
        var height = logo.BoundingBox.Height > 0 ? Convert.ToInt64(logo.BoundingBox.Height * EmusPerPixel) : DefaultImageHeight;

        var extent = new DW.Extent { Cx = width, Cy = height };
        var effectExtent = new DW.EffectExtent
        {
            LeftEdge = 0L,
            TopEdge = 0L,
            RightEdge = 0L,
            BottomEdge = 0L
        };

        var docProperties = new DW.DocProperties
        {
            Id = (uint)(logo.BoundingBox.PageNumber + 1),
            Name = logo.AssetId,
            Title = logo.AssetId
        };

        var nonVisualProps = new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true });

        var nonVisualPictureProps = new PIC.NonVisualPictureProperties(
            new PIC.NonVisualDrawingProperties
            {
                Id = (uint)(logo.BoundingBox.PageNumber + 100),
                Name = $"Picture {logo.AssetId}"
            },
            new PIC.NonVisualPictureDrawingProperties());

        var blipFill = new PIC.BlipFill(
            new A.Blip { Embed = relationshipId },
            new A.Stretch(new A.FillRectangle()));

        var shapeProperties = new PIC.ShapeProperties(
            new A.Transform2D(
                new A.Offset { X = 0L, Y = 0L },
                new A.Extents { Cx = width, Cy = height }),
            new A.PresetGeometry(new A.AdjustValueList())
            { Preset = A.ShapeTypeValues.Rectangle });

        var picture = new PIC.Picture(nonVisualPictureProps, blipFill, shapeProperties);
        var graphicData = new A.GraphicData(picture)
        {
            Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
        };

        var graphic = new A.Graphic(graphicData);

        var inline = new DW.Inline(extent, effectExtent, docProperties, nonVisualProps, graphic)
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U
        };

        return new Drawing(inline);
    }

    private static void ApplyDefaultStyles(MainDocumentPart mainPart, VisualTheme theme)
    {
        var stylesPart = mainPart.StyleDefinitionsPart ?? mainPart.AddNewPart<StyleDefinitionsPart>();
        
        if (stylesPart.Styles == null)
        {
            stylesPart.Styles = new Styles();
        }

        // Ensure DefaultParagraphFont is present (required by Word)
        var bodyFont = GetBodyFont(theme);
        if (!stylesPart.Styles.Elements<DocDefaults>().Any())
        {
            var docDefaults = new DocDefaults();
            var defaultRunProps = new RunPropertiesDefault();
            var runProps = new RunProperties();
            runProps.Append(new RunFonts { Ascii = bodyFont.Family, HighAnsi = bodyFont.Family });
            var defaultSize = ((int)Math.Round(bodyFont.Size * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            runProps.Append(new FontSize { Val = defaultSize });
            runProps.Append(new FontSizeComplexScript { Val = defaultSize });
            defaultRunProps.Append(runProps);
            docDefaults.Append(defaultRunProps);
            stylesPart.Styles.Append(docDefaults);
        }

        // Create or update all styles from theme to preserve original formatting
        foreach (var fontEntry in theme.FontMap)
        {
            var styleId = fontEntry.Key;
            var fontDef = fontEntry.Value;

            // Map style names to valid Word style IDs
            var mappedStyleId = MapStyleNameToId(styleId);
            var existingStyle = stylesPart.Styles.Elements<Style>().FirstOrDefault(s => s.StyleId == mappedStyleId);

            if (existingStyle == null)
            {
                existingStyle = new Style
                {
                    Type = StyleValues.Paragraph,
                    StyleId = mappedStyleId,
                    StyleName = new StyleName { Val = styleId }
                };

                // Mark Normal as default
                if (string.Equals(styleId, "Normal", StringComparison.OrdinalIgnoreCase))
                {
                    existingStyle.Default = true;
                    existingStyle.UIPriority = new UIPriority { Val = 99 };
                    existingStyle.PrimaryStyle = new PrimaryStyle();
                }
                else
                {
                    existingStyle.UIPriority = new UIPriority { Val = GetUIPriorityForStyle(styleId) };
                }

                // Set BasedOn for headings
                if (styleId.StartsWith("heading", StringComparison.OrdinalIgnoreCase))
                {
                    existingStyle.BasedOn = new BasedOn { Val = "Normal" };
                }

                stylesPart.Styles.Append(existingStyle);
            }

            // Update style run properties
            if (existingStyle.StyleRunProperties == null)
            {
                existingStyle.StyleRunProperties = new StyleRunProperties();
            }
            else
            {
                existingStyle.StyleRunProperties.RemoveAllChildren();
            }

            // Build style run properties in correct order
            if (!string.IsNullOrWhiteSpace(fontDef.Family))
            {
                existingStyle.StyleRunProperties.Append(new RunFonts { Ascii = fontDef.Family, HighAnsi = fontDef.Family, EastAsia = fontDef.Family });
            }

            if (ShouldBold(fontDef))
            {
                existingStyle.StyleRunProperties.Append(new Bold());
            }

            if (fontDef.Size > 0)
            {
                var sizeValue = ((int)Math.Round(fontDef.Size * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
                existingStyle.StyleRunProperties.Append(new FontSize { Val = sizeValue });
                existingStyle.StyleRunProperties.Append(new FontSizeComplexScript { Val = sizeValue });
            }

            if (!string.IsNullOrWhiteSpace(fontDef.Color))
            {
                var colorValue = NormalizeColorValue(fontDef.Color);
                existingStyle.StyleRunProperties.Append(new Color { Val = colorValue });
            }
        }
    }

    private static string MapStyleNameToId(string styleName)
    {
        // Map common style names to valid Word style IDs
        return styleName.ToLowerInvariant() switch
        {
            "heading 1" => "Heading1",
            "heading 2" => "Heading2",
            "heading 3" => "Heading3",
            "heading 4" => "Heading4",
            "heading 5" => "Heading5",
            "heading 6" => "Heading6",
            "normal" => "Normal",
            "title" => "Title",
            "subtitle" => "Subtitle",
            _ => styleName.Replace(" ", "").Replace("-", "")
        };
    }

    private static int GetUIPriorityForStyle(string styleName)
    {
        if (styleName.StartsWith("heading", StringComparison.OrdinalIgnoreCase))
        {
            var depth = styleName.ToLowerInvariant() switch
            {
                "heading 1" => 1,
                "heading 2" => 2,
                "heading 3" => 3,
                "heading 4" => 4,
                "heading 5" => 5,
                "heading 6" => 6,
                _ => 1
            };
            return 10 - depth;
        }
        return 99;
    }

    private static string NormalizeColorValue(string color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return "000000";
        }

        var trimmed = color.TrimStart('#').ToUpperInvariant();
        
        // Handle 3-digit hex (e.g., #FFF -> FFFFFF)
        if (trimmed.Length == 3)
        {
            return new string(trimmed.SelectMany(c => new[] { c, c }).ToArray());
        }

        // Handle 6-digit hex
        if (trimmed.Length == 6)
        {
            return trimmed;
        }

        // Default to black if invalid
        return "000000";
    }

    private static string GetPlaceholderToken(string placeholderId) => $"{{{{{placeholderId}}}}}";

    private static long MillimetersToTwips(double millimeters) => (long)Math.Round(millimeters * 56.7, MidpointRounding.AwayFromZero);

    private static readonly Regex PlaceholderRegex = new(@"\{\{(?<id>[^}]+)\}\}", RegexOptions.Compiled);
    private const long DefaultImageWidth = 2_000_000L;
    private const long DefaultImageHeight = 2_000_000L;
    private const double EmusPerPixel = 9525d;
}

public sealed class DocumentRenderRequest
{
    public Dictionary<string, string> SectionContent { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<LogoReplacement> LogoReplacements { get; } = new();
}

public sealed record LogoReplacement(string AssetId, byte[] Content, string ContentType);

