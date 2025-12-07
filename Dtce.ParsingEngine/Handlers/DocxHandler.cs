using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Dtce.ParsingEngine.Handlers;

public class DocxHandler : IDocumentHandler
{
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<DocxHandler> _logger;

    private static readonly Regex NumberedHeadingRegex = new(@"^(?<token>(\d+(\.\d+)*|[A-Z]\)|[IVXLC]+\.)\s+)", RegexOptions.Compiled);
    private static readonly Regex BulletedHeadingRegex = new(@"^(\-|\*|•)\s+\S+", RegexOptions.Compiled);
    
    // Document structure analyzer - dynamically analyzes paragraphs to identify heading patterns
    private DocumentStructureAnalyzer? _structureAnalyzer;

    public DocxHandler(IObjectStorage objectStorage, ILogger<DocxHandler> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
    }

    public async Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing DOCX document for job {JobId}", jobRequest.JobId);

        if (string.IsNullOrEmpty(jobRequest.FilePath))
        {
            throw new InvalidOperationException("File path is required for DOCX parsing");
        }

        // Download the document
        using var documentStream = await _objectStorage.DownloadFileAsync(jobRequest.FilePath, cancellationToken);
        using var memoryStream = new MemoryStream();
        await documentStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        var result = new ParseResult
        {
            TemplateJson = new TemplateJson(),
            ContentSections = new List<ContentSection>()
        };

        using (var wordDocument = WordprocessingDocument.Open(memoryStream, false))
        {
            var mainPart = wordDocument.MainDocumentPart;
            if (mainPart == null)
            {
                throw new InvalidOperationException("Document does not have a main part");
            }

            // Extract visual theme (styles, fonts, colors)
            result.TemplateJson.VisualTheme = ExtractVisualTheme(wordDocument);

            // Extract layout rules (page settings, margins)
            result.TemplateJson.VisualTheme.LayoutRules = ExtractLayoutRules(wordDocument);

            // Analyze document structure dynamically first
            _structureAnalyzer = new DocumentStructureAnalyzer(_logger);
            _structureAnalyzer.AnalyzeDocument(mainPart);

            // Extract structural hierarchy and content
            var (sections, contentSections) = ExtractStructureAndContent(mainPart);
            result.TemplateJson.SectionHierarchy = new SectionHierarchy { Sections = sections };
            result.ContentSections = contentSections;

            // Extract logos and images
            result.TemplateJson.LogoMap = await ExtractImagesAsync(wordDocument, jobRequest.JobId, cancellationToken);
        }

        _logger.LogInformation("Successfully parsed DOCX document: {SectionCount} sections, {ContentCount} content blocks",
            result.TemplateJson.SectionHierarchy.Sections.Count, result.ContentSections.Count);

        return result;
    }

    private VisualTheme ExtractVisualTheme(WordprocessingDocument document)
    {
        var theme = new VisualTheme
        {
            ColorPalette = new List<ColorDefinition>(),
            FontMap = new Dictionary<string, FontDefinition>(),
            LayoutRules = new LayoutRules()
        };

        var stylesPart = document.MainDocumentPart?.StyleDefinitionsPart;
        if (stylesPart?.Styles == null)
        {
            _logger.LogWarning("Document does not have styles part");
            return theme;
        }

        var colorSet = new HashSet<string>();
        var fontSet = new HashSet<string>();

        foreach (var style in stylesPart.Styles.Elements<Style>())
        {
            var styleId = style.StyleId?.Value ?? "Unknown";
            var styleName = style.StyleName?.Val?.Value ?? styleId;

            var fontDefinition = new FontDefinition
            {
                Family = "Calibri", // Default
                Size = 11, // Default
                Weight = "normal",
                Color = "#000000" // Default black
            };

            // Extract font properties from style
            var runProperties = style.StyleRunProperties;
            if (runProperties != null)
            {
                // Font family
                if (runProperties.RunFonts?.Ascii?.Value != null)
                {
                    fontDefinition.Family = runProperties.RunFonts.Ascii.Value;
                }

                // Font size (in half-points, convert to points)
                if (runProperties.FontSize?.Val != null)
                {
                    var fontSizeValue = runProperties.FontSize.Val.Value;
                    if (int.TryParse(fontSizeValue, out var parsedSize))
                    {
                        fontDefinition.Size = parsedSize / 2.0;
                    }
                }

                // Font weight
                if (runProperties.Bold != null)
                {
                    fontDefinition.Weight = "bold";
                }

                // Font color
                if (runProperties.Color?.Val != null)
                {
                    var colorValue = runProperties.Color.Val.Value;
                    if (colorValue != null && colorValue.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        fontDefinition.Color = "#000000";
                    }
                    else if (colorValue != null)
                    {
                        fontDefinition.Color = ConvertRgbToHex(colorValue);
                        if (!colorSet.Contains(fontDefinition.Color))
                        {
                            colorSet.Add(fontDefinition.Color);
                        }
                    }
                }
            }

            theme.FontMap[styleName] = fontDefinition;

            // Extract font family for color palette
            if (!string.IsNullOrEmpty(fontDefinition.Family) && !fontSet.Contains(fontDefinition.Family))
            {
                fontSet.Add(fontDefinition.Family);
            }
        }

        // Build color palette from extracted colors
        var colorList = colorSet.ToList();
        if (colorList.Count > 0)
        {
            theme.ColorPalette.Add(new ColorDefinition { Name = "primary", HexCode = colorList[0] });
        }
        if (colorList.Count > 1)
        {
            theme.ColorPalette.Add(new ColorDefinition { Name = "secondary", HexCode = colorList[1] });
        }
        if (colorList.Count > 2)
        {
            theme.ColorPalette.Add(new ColorDefinition { Name = "accent", HexCode = colorList[2] });
        }

        return theme;
    }

    private LayoutRules ExtractLayoutRules(WordprocessingDocument document)
    {
        var layoutRules = new LayoutRules
        {
            PageWidth = 210, // Default A4 width in mm
            PageHeight = 297, // Default A4 height in mm
            Orientation = "portrait",
            Margins = new MarginDefinition
            {
                Top = 25.4, // Default 1 inch in mm
                Bottom = 25.4,
                Left = 25.4,
                Right = 25.4
            }
        };

        var mainPart = document.MainDocumentPart;
        if (mainPart?.Document?.Body == null)
        {
            return layoutRules;
        }

        // Extract section properties
        var sectionProperties = mainPart.Document.Body.Elements<SectionProperties>().FirstOrDefault();
        if (sectionProperties != null)
        {
            // Get page size
            var pageSize = sectionProperties.Descendants<PageSize>().FirstOrDefault();
            if (pageSize != null)
            {
                if (pageSize.Width != null && pageSize.Height != null)
                {
                    // Convert from twips (1/20th of a point) to millimeters
                    // 1 twip = 1/1440 inch = 0.01764 mm
                    layoutRules.PageWidth = ConvertTwipsToMillimeters(pageSize.Width.Value);
                    layoutRules.PageHeight = ConvertTwipsToMillimeters(pageSize.Height.Value);

                    if (pageSize.Orient?.Value == PageOrientationValues.Landscape)
                    {
                        layoutRules.Orientation = "landscape";
                        // Swap width and height for landscape
                        (layoutRules.PageWidth, layoutRules.PageHeight) = (layoutRules.PageHeight, layoutRules.PageWidth);
                    }
                }
            }

            // Extract margins
            var pageMargin = sectionProperties.Descendants<PageMargin>().FirstOrDefault();
            if (pageMargin != null)
            {
                if (pageMargin.Top != null) layoutRules.Margins.Top = ConvertTwipsToMillimeters(pageMargin.Top.Value);
                if (pageMargin.Bottom != null) layoutRules.Margins.Bottom = ConvertTwipsToMillimeters(pageMargin.Bottom.Value);
                if (pageMargin.Left != null) layoutRules.Margins.Left = ConvertTwipsToMillimeters(pageMargin.Left.Value);
                if (pageMargin.Right != null) layoutRules.Margins.Right = ConvertTwipsToMillimeters(pageMargin.Right.Value);
            }
        }

        return layoutRules;
    }

    private (List<Section> sections, List<ContentSection> contentSections) ExtractStructureAndContent(MainDocumentPart mainPart)
    {
        var sections = new List<Section>();
        var contentSections = new List<ContentSection>();

        if (mainPart.Document?.Body == null)
        {
            return (sections, contentSections);
        }

        var sectionStack = new Stack<SectionContext>();
        var sectionCounter = 0;

        SectionContext CreateSection(string title, int level)
        {
            sectionCounter++;
            var placeholderId = level == 1
                ? $"placeholder_section_{sectionCounter}"
                : $"placeholder_subsection_{sectionCounter}";

            var section = new Section
            {
                SectionTitle = title,
                PlaceholderId = placeholderId,
                SubSections = new List<Section>()
            };

            return new SectionContext(section, level, placeholderId);
        }

        SectionContext EnsureRootSection()
        {
            if (sectionStack.Count > 0)
            {
                return sectionStack.Peek();
            }

            var root = new Section
            {
                SectionTitle = "Document Content",
                PlaceholderId = "placeholder_document_content",
                SubSections = new List<Section>()
            };

            sections.Add(root);
            var context = new SectionContext(root, 1, root.PlaceholderId);
            sectionStack.Push(context);
            return context;
        }

        void FinalizeSection(SectionContext context)
        {
            var text = context.ContentBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            contentSections.Add(new ContentSection
            {
                PlaceholderId = context.PlaceholderId,
                SectionTitle = context.Section.SectionTitle,
                SampleText = text,
                WordCount = CountWords(text)
            });
        }

        foreach (var element in mainPart.Document.Body.Elements())
        {
            if (element is not Paragraph paragraph)
            {
                continue;
            }

            var paragraphText = GetParagraphText(paragraph)?.Trim();
            if (string.IsNullOrWhiteSpace(paragraphText))
            {
                continue;
            }

            var headingLevel = DetermineHeadingLevel(paragraph, paragraphText, sectionStack);
            if (headingLevel > 0)
            {
                var cleanedTitle = NormalizeHeadingText(paragraphText);
                if (string.IsNullOrWhiteSpace(cleanedTitle))
                {
                    cleanedTitle = paragraphText.Trim();
                }

                while (sectionStack.Count > 0 && sectionStack.Peek().Level >= headingLevel)
                {
                    var finished = sectionStack.Pop();
                    FinalizeSection(finished);
                }

                var context = CreateSection(cleanedTitle, headingLevel);

                if (sectionStack.Count == 0)
                {
                    sections.Add(context.Section);
                }
                else
                {
                    sectionStack.Peek().Section.SubSections ??= new List<Section>();
                    sectionStack.Peek().Section.SubSections.Add(context.Section);
                }

                sectionStack.Push(context);
                continue;
            }

            var activeSection = sectionStack.Count > 0 ? sectionStack.Peek() : EnsureRootSection();
            if (activeSection.ContentBuilder.Length > 0)
            {
                activeSection.ContentBuilder.AppendLine();
            }
            activeSection.ContentBuilder.Append(paragraphText);
        }

        while (sectionStack.Count > 0)
        {
            var context = sectionStack.Pop();
            FinalizeSection(context);
        }

        if (sections.Count == 0 && contentSections.Count == 0)
        {
            var allText = new StringBuilder();
            foreach (var paragraph in mainPart.Document.Body.Elements<Paragraph>())
            {
                var text = GetParagraphText(paragraph);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (allText.Length > 0)
                    {
                        allText.AppendLine();
                    }
                    allText.Append(text.Trim());
                }
            }

            var fullText = allText.ToString().Trim();
            if (!string.IsNullOrEmpty(fullText))
            {
                const string placeholder = "placeholder_document_content";
                sections.Add(new Section
                {
                    SectionTitle = "Document Content",
                    PlaceholderId = placeholder,
                    SubSections = new List<Section>()
                });

                contentSections.Add(new ContentSection
                {
                    PlaceholderId = placeholder,
                    SectionTitle = "Document Content",
                    SampleText = fullText,
                    WordCount = CountWords(fullText)
                });
            }
        }

        return (sections, contentSections);
    }

    private string GetParagraphText(Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var run in paragraph.Elements<Run>())
        {
            foreach (var textElement in run.Elements<Text>())
            {
                text.Append(textElement.Text);
            }
        }
        return text.ToString();
    }

    private sealed class SectionContext
    {
        public SectionContext(Section section, int level, string placeholderId)
        {
            Section = section;
            Level = level;
            PlaceholderId = placeholderId;
            ContentBuilder = new StringBuilder();
        }

        public Section Section { get; }
        public int Level { get; }
        public string PlaceholderId { get; }
        public StringBuilder ContentBuilder { get; }
    }

    private int DetermineHeadingLevel(Paragraph paragraph, string text, Stack<SectionContext> sectionStack)
    {
        // Use dynamic structure analyzer if available
        if (_structureAnalyzer != null)
        {
            var currentStackLevel = sectionStack.Count == 0 ? 0 : sectionStack.Peek().Level;
            return _structureAnalyzer.DetermineHeadingLevel(paragraph, text, currentStackLevel);
        }

        // Fallback to basic heuristics if analyzer not initialized
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (TryGetHeadingLevelFromStyle(styleId, out var styledLevel))
        {
            return styledLevel;
        }

        if (BulletedHeadingRegex.IsMatch(text))
        {
            return 0;
        }

        if (IsLikelyBodyText(text))
        {
            return 0;
        }

        var normalized = NormalizeHeadingText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        if (TryGetNumberedHeadingLevel(normalized, out var numberedLevel))
        {
            return numberedLevel;
        }

        if (HasHeadingFormatting(paragraph))
        {
            return sectionStack.Count == 0 ? 1 : sectionStack.Peek().Level;
        }

        if (IsUppercaseHeading(normalized))
        {
            var parent = sectionStack.Count == 0 ? 1 : sectionStack.Peek().Level;
            return Math.Clamp(parent, 1, 6);
        }

        var endedWithColon = text.TrimEnd().EndsWith(":", StringComparison.Ordinal);
        if (endedWithColon || LooksLikeStandaloneHeading(normalized))
        {
            var parent = sectionStack.Count == 0 ? 0 : sectionStack.Peek().Level;
            return Math.Clamp(parent + 1, 1, 6);
        }

        return 0;
    }

    private bool TryGetHeadingLevelFromStyle(string? styleId, out int level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(styleId))
        {
            return false;
        }

        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(styleId.Where(char.IsDigit).ToArray());
            level = int.TryParse(digits, out var parsed) ? parsed : 1;
            level = Math.Clamp(level, 1, 6);
            return true;
        }

        if (styleId.StartsWith("Title", StringComparison.OrdinalIgnoreCase))
        {
            level = 1;
            return true;
        }

        return false;
    }

    private static double CalculateUppercaseRatio(string text)
    {
        var uppercase = 0;
        var letters = 0;

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (char.IsUpper(ch))
                {
                    uppercase++;
                }
            }
        }

        return letters == 0 ? 0 : (double)uppercase / letters;
    }

    private bool HasHeadingFormatting(Paragraph paragraph)
    {
        var runProperties = paragraph.Elements<Run>().FirstOrDefault()?.RunProperties;
        if (runProperties == null) return false;

        // Check for bold and larger font size (common heading indicators)
        var isBold = runProperties.Bold != null;
        var fontSize = 0;
        if (runProperties.FontSize?.Val != null)
        {
            var fontSizeValue = runProperties.FontSize.Val.Value;
            if (int.TryParse(fontSizeValue, out var parsedSize))
            {
                fontSize = parsedSize;
            }
        }
        var isLargerFont = fontSize > 220; // 11pt = 220 half-points, headings are usually larger

        return isBold && isLargerFont;
    }

    private static bool IsUppercaseHeading(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12 &&
        CalculateUppercaseRatio(text) >= 0.6;

    private static bool LooksLikeStandaloneHeading(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 14)
        {
            return false;
        }

        if (text.Contains('.', StringComparison.Ordinal) || text.Contains(';', StringComparison.Ordinal) ||
            text.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        return char.IsLetterOrDigit(text.FirstOrDefault());
    }

    private static bool IsLikelyBodyText(string text)
    {
        var trimmed = text.Trim();
        var wordCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 30)
        {
            return true;
        }

        if (trimmed.Count(c => c == '.') >= 2)
        {
            return true;
        }

        if (trimmed.Contains(". ") && trimmed.Length > 80)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetNumberedHeadingLevel(string text, out int level)
    {
        level = 0;
        var match = NumberedHeadingRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var token = match.Groups["token"].Value.Trim().TrimEnd('.', ')');
        if (string.IsNullOrWhiteSpace(token))
        {
            level = 1;
            return true;
        }

        if (token.Contains('.', StringComparison.Ordinal))
        {
            var segments = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            level = Math.Clamp(segments.Length, 1, 6);
            return true;
        }

        if (int.TryParse(token, out _))
        {
            level = 1;
            return true;
        }

        if (token.Length == 1 && char.IsLetter(token[0]))
        {
            level = 2;
            return true;
        }

        level = 1;
        return true;
    }

    private static string NormalizeHeadingText(string text) =>
        text.Trim().TrimEnd(':', '-', '–').Trim();

    private async Task<List<LogoAsset>> ExtractImagesAsync(WordprocessingDocument document, string jobId, CancellationToken cancellationToken)
    {
        var logoAssets = new List<LogoAsset>();
        var imageCounter = 0;

        var mainPart = document.MainDocumentPart;
        if (mainPart == null) return logoAssets;

        // Extract images from document parts
        foreach (var imagePart in mainPart.ImageParts)
        {
            try
            {
                imageCounter++;
                var imageId = mainPart.GetIdOfPart(imagePart);
                var assetId = $"asset_{jobId}_{imageCounter}";

                // Save image to object storage
                var imageFileName = $"images/{jobId}/{assetId}.{GetImageExtension(imagePart)}";
                using var imageStream = imagePart.GetStream();
                var imageUrl = await _objectStorage.UploadFileAsync(
                    imageFileName,
                    imageStream,
                    imagePart.ContentType,
                    cancellationToken);

                // Try to find the image in the document to get position
                var drawing = FindDrawingByImageId(mainPart, imageId);
                var boundingBox = new BoundingBox
                {
                    X = 0,
                    Y = 0,
                    Width = 100, // Default values, would need actual positioning
                    Height = 100,
                    PageNumber = 1
                };

                if (drawing != null)
                {
                    // Extract actual dimensions if available
                    // Look for extent in inline or anchor
                    var inline = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline>().FirstOrDefault();
                    if (inline?.Extent != null)
                    {
                        // Convert from EMU (English Metric Units) to pixels
                        // 1 EMU = 1/914400 inch, 96 DPI
                        if (inline.Extent.Cx != null)
                            boundingBox.Width = inline.Extent.Cx.Value / 914400.0 * 96.0;
                        if (inline.Extent.Cy != null)
                            boundingBox.Height = inline.Extent.Cy.Value / 914400.0 * 96.0;
                    }
                }

                logoAssets.Add(new LogoAsset
                {
                    AssetId = assetId,
                    AssetType = "image", // Could be enhanced to detect if it's a logo
                    BoundingBox = boundingBox,
                    SecureUrl = imageUrl,
                    StorageKey = imageFileName
                });

                _logger.LogInformation("Extracted image {AssetId} from document", assetId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract image {ImageCounter}", imageCounter);
            }
        }

        return logoAssets;
    }

    private Drawing? FindDrawingByImageId(MainDocumentPart mainPart, string imageId)
    {
        if (mainPart.Document?.Body == null) return null;

        foreach (var paragraph in mainPart.Document.Body.Descendants<Paragraph>())
        {
            var drawing = paragraph.Descendants<Drawing>().FirstOrDefault(d =>
            {
                var blip = d.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
                return blip?.Embed?.Value == imageId;
            });

            if (drawing != null) return drawing;
        }

        return null;
    }

    private string GetImageExtension(ImagePart imagePart)
    {
        return imagePart.ContentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            _ => "png"
        };
    }

    private string ConvertRgbToHex(string rgbValue)
    {
        // Handle auto color
        if (rgbValue.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
        {
            return "#000000";
        }

        // Handle hex values (some DOCX files store colors as hex)
        if (rgbValue.StartsWith("#"))
        {
            return rgbValue;
        }

        // Handle RGB values (format: "RRGGBB" or "AARRGGBB")
        if (rgbValue.Length == 6 || rgbValue.Length == 8)
        {
            return "#" + rgbValue;
        }

        // Try to parse as integer RGB
        if (int.TryParse(rgbValue, out var rgbInt))
        {
            var r = (rgbInt >> 16) & 0xFF;
            var g = (rgbInt >> 8) & 0xFF;
            var b = rgbInt & 0xFF;
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        return "#000000"; // Default to black
    }

    private double ConvertTwipsToMillimeters(long twips)
    {
        // 1 twip = 1/1440 inch = 0.01764 mm
        return twips * 0.01764;
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return Regex.Matches(text, @"\b\w+\b").Count;
    }
}
