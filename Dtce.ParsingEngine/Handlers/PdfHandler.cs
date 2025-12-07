using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dtce.ParsingEngine.Handlers;

public class PdfHandler : IDocumentHandler
{
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<PdfHandler> _logger;

    public PdfHandler(IObjectStorage objectStorage, ILogger<PdfHandler> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
    }

    public async Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing PDF document for job {JobId}", jobRequest.JobId);

        if (string.IsNullOrWhiteSpace(jobRequest.FilePath))
        {
            throw new InvalidOperationException("JobRequest.FilePath is required for PDF parsing.");
        }

        await using var sourceStream = await _objectStorage.DownloadFileAsync(jobRequest.FilePath, cancellationToken);
        using var memory = new MemoryStream();
        await sourceStream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        using var document = PdfDocument.Open(memory);

        var sections = new List<Section>();
        var contentSections = new List<ContentSection>();
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            var text = page.Text;

            var placeholderId = $"section_pdf_{pageNumber}";
            var section = new Section
            {
                SectionTitle = $"Page {pageNumber}",
                PlaceholderId = placeholderId,
                SubSections = new List<Section>()
            };
            sections.Add(section);

            contentSections.Add(new ContentSection
            {
                PlaceholderId = placeholderId,
                SectionTitle = section.SectionTitle,
                SampleText = text.Length > 600 ? text[..600] : text,
                WordCount = CountWords(text)
            });
        }

        var firstPage = document.GetPage(1);
        var visualTheme = new VisualTheme
        {
            ColorPalette = new List<ColorDefinition>
            {
                new() { Name = "primary", HexCode = "#000000" },
                new() { Name = "secondary", HexCode = "#444444" },
                new() { Name = "accent", HexCode = "#1a73e8" }
            },
            FontMap = ExtractFontDefinitions(document),
            LayoutRules = new LayoutRules
            {
                PageWidth = Math.Round(firstPage.Width, 2),
                PageHeight = Math.Round(firstPage.Height, 2),
                Orientation = firstPage.Width > firstPage.Height ? "landscape" : "portrait",
                Margins = new MarginDefinition
                {
                    Top = 20,
                    Bottom = 20,
                    Left = 20,
                    Right = 20
                }
            }
        };

        var result = new ParseResult
        {
            TemplateJson = new TemplateJson
            {
                VisualTheme = visualTheme,
                SectionHierarchy = new SectionHierarchy { Sections = sections },
                LogoMap = new List<LogoAsset>()
            },
            ContentSections = contentSections
        };

        return result;
    }

    private static Dictionary<string, FontDefinition> ExtractFontDefinitions(PdfDocument document)
    {
        var fonts = new Dictionary<string, FontDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var page in document.GetPages())
        {
            foreach (var letter in page.Letters.Take(250))
            {
                var fontName = string.IsNullOrWhiteSpace(letter.FontName) ? "Body" : letter.FontName;
                if (fonts.ContainsKey(fontName))
                {
                    continue;
                }

                fonts[fontName] = new FontDefinition
                {
                    Family = fontName,
                    Size = Math.Round(letter.PointSize, 2),
                    Weight = letter.PointSize > 13 ? "bold" : "normal",
                    Color = "#000000"
                };
            }

            if (fonts.Count >= 5)
            {
                break;
            }
        }

        if (fonts.Count == 0)
        {
            fonts["Body"] = new FontDefinition
            {
                Family = "Helvetica",
                Size = 12,
                Weight = "normal",
                Color = "#000000"
            };
        }

        return fonts;
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
}

