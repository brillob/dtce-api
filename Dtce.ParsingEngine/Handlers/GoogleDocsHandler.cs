using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using HtmlAgilityPack;

namespace Dtce.ParsingEngine.Handlers;

public class GoogleDocsHandler : IDocumentHandler
{
    private readonly IObjectStorage _objectStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleDocsHandler> _logger;

    public GoogleDocsHandler(
        IObjectStorage objectStorage,
        IHttpClientFactory httpClientFactory,
        ILogger<GoogleDocsHandler> logger)
    {
        _objectStorage = objectStorage;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Parsing Google Docs document for job {JobId}", jobRequest.JobId);

        if (string.IsNullOrWhiteSpace(jobRequest.DocumentUrl))
        {
            throw new InvalidOperationException("DocumentUrl is required for Google Docs parsing.");
        }

        var exportUri = BuildExportUri(jobRequest.DocumentUrl);
        var client = _httpClientFactory.CreateClient(nameof(GoogleDocsHandler));

        using var response = await client.GetAsync(exportUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to download Google Doc export. Status code: {response.StatusCode}");
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = new HtmlDocument();
        document.LoadHtml(html);

        var sections = BuildSectionHierarchy(document);
        var contentSections = ExtractContentSections(document, sections);
        var visualTheme = ExtractVisualTheme(document);
        var logoAssets = await ExtractImagesAsync(client, document, jobRequest.JobId, cancellationToken);

        var result = new ParseResult
        {
            TemplateJson = new TemplateJson
            {
                VisualTheme = visualTheme,
                SectionHierarchy = new SectionHierarchy { Sections = sections },
                LogoMap = logoAssets
            },
            ContentSections = contentSections
        };

        return result;
    }

    private static Uri BuildExportUri(string documentUrl)
    {
        if (!Uri.TryCreate(documentUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("DocumentUrl must be an absolute URL.");
        }

        var segments = uri.Segments.Select(s => s.Trim('/')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        var docIndex = Array.IndexOf(segments, "document");
        if (docIndex < 0 || docIndex + 1 >= segments.Length || segments[docIndex + 1] != "d")
        {
            throw new InvalidOperationException("DocumentUrl does not match the expected Google Docs format.");
        }

        var docId = segments[docIndex + 2];
        var exportUri = new Uri($"https://docs.google.com/document/d/{docId}/export?format=html");
        return exportUri;
    }

    private static List<Section> BuildSectionHierarchy(HtmlDocument document)
    {
        var headingNodes = document.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4");
        var sections = new List<Section>();

        if (headingNodes == null)
        {
            sections.Add(new Section
            {
                SectionTitle = "Document Body",
                PlaceholderId = "section_doc_body"
            });
            return sections;
        }

        var stack = new Stack<(int level, Section section)>();
        var headingIndex = 0;

        foreach (var node in headingNodes)
        {
            var level = int.Parse(node.Name[1].ToString());
            var title = NormalizeText(node.InnerText);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            headingIndex++;
            var placeholderId = $"section_gdoc_{headingIndex}";
            var section = new Section
            {
                SectionTitle = title,
                PlaceholderId = placeholderId,
                SubSections = new List<Section>()
            };

            while (stack.Count > 0 && stack.Peek().level >= level)
            {
                stack.Pop();
            }

            if (stack.Count == 0)
            {
                sections.Add(section);
            }
            else
            {
                stack.Peek().section.SubSections.Add(section);
            }

            stack.Push((level, section));
        }

        if (sections.Count == 0)
        {
            sections.Add(new Section
            {
                SectionTitle = "Document Body",
                PlaceholderId = "section_doc_body"
            });
        }

        return sections;
    }

    private static List<ContentSection> ExtractContentSections(HtmlDocument document, List<Section> sections)
    {
        var sectionLookup = FlattenSections(sections).ToDictionary(s => s.PlaceholderId, s => s);
        var contentMap = sectionLookup.Keys.ToDictionary(id => id, _ => new StringBuilder(), StringComparer.OrdinalIgnoreCase);

        var orderedNodes = document.DocumentNode.SelectNodes("//body//*[self::h1 or self::h2 or self::h3 or self::h4 or self::p]")
            ?? document.DocumentNode.SelectNodes("//p")
            ?? new HtmlNodeCollection(document.DocumentNode);

        string currentPlaceholder = sections.FirstOrDefault()?.PlaceholderId ?? "section_doc_body";

        foreach (var node in orderedNodes)
        {
            if (node.Name.StartsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                var headingText = NormalizeText(node.InnerText);
                if (string.IsNullOrWhiteSpace(headingText))
                {
                    continue;
                }

                var placeholder = sectionLookup.Values.FirstOrDefault(s =>
                    string.Equals(s.SectionTitle, headingText, StringComparison.OrdinalIgnoreCase));

                if (placeholder != null)
                {
                    currentPlaceholder = placeholder.PlaceholderId;
                }
            }
            else if (node.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
            {
                var text = NormalizeText(node.InnerText);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!contentMap.TryGetValue(currentPlaceholder, out var builder))
                {
                    builder = new StringBuilder();
                    contentMap[currentPlaceholder] = builder;
                }
                builder.AppendLine(text);
            }
        }

        var contentSections = new List<ContentSection>();
        foreach (var kvp in contentMap)
        {
            var text = kvp.Value.ToString().Trim();
            var section = sectionLookup.TryGetValue(kvp.Key, out var sec) ? sec : null;

            contentSections.Add(new ContentSection
            {
                PlaceholderId = kvp.Key,
                SectionTitle = section?.SectionTitle ?? kvp.Key,
                SampleText = text.Length > 500 ? text[..500] : text,
                WordCount = CountWords(text)
            });
        }

        return contentSections;
    }

    private static VisualTheme ExtractVisualTheme(HtmlDocument document)
    {
        var colorRegex = new Regex(@"color\s*:\s*(#[0-9a-fA-F]{3,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var fontRegex = new Regex(@"font-family\s*:\s*([^;\""']+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        var colorMatches = colorRegex.Matches(document.DocumentNode.InnerHtml);
        var colors = colorMatches.Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(code => code.StartsWith("#"))
            .Distinct()
            .Take(6)
            .Select((code, index) => new ColorDefinition
            {
                Name = index switch
                {
                    0 => "primary",
                    1 => "secondary",
                    2 => "accent",
                    _ => $"color_{index}"
                },
                HexCode = code
            })
            .ToList();

        if (colors.Count == 0)
        {
            colors = new List<ColorDefinition>
            {
                new() { Name = "primary", HexCode = "#1a73e8" },
                new() { Name = "secondary", HexCode = "#202124" },
                new() { Name = "accent", HexCode = "#fbbc04" }
            };
        }

        var fonts = new Dictionary<string, FontDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in fontRegex.Matches(document.DocumentNode.InnerHtml))
        {
            var font = match.Groups[1].Value.Trim('\'', '"', ' ');
            if (string.IsNullOrWhiteSpace(font) || fonts.ContainsKey(font))
            {
                continue;
            }

            fonts[font] = new FontDefinition
            {
                Family = font,
                Size = 12,
                Weight = "normal",
                Color = colors.First().HexCode
            };
        }

        if (fonts.Count == 0)
        {
            fonts["Body"] = new FontDefinition
            {
                Family = "Roboto",
                Size = 12,
                Weight = "normal",
                Color = colors.First().HexCode
            };
        }

        return new VisualTheme
        {
            ColorPalette = colors,
            FontMap = fonts,
            LayoutRules = new LayoutRules
            {
                PageWidth = 210,
                PageHeight = 297,
                Orientation = "portrait",
                Margins = new MarginDefinition { Top = 25, Bottom = 25, Left = 25, Right = 25 }
            }
        };
    }

    private async Task<List<LogoAsset>> ExtractImagesAsync(HttpClient client, HtmlDocument document, string jobId, CancellationToken cancellationToken)
    {
        var images = document.DocumentNode.SelectNodes("//img");
        var results = new List<LogoAsset>();
        if (images == null)
        {
            return results;
        }

        var index = 0;
        foreach (var img in images)
        {
            var source = img.GetAttributeValue("src", null);
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            index++;
            var storageKey = $"images/{jobId}/google_{index}.png";
            string secureUrl;

            if (source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var (mediaType, data) = ParseDataUri(source);
                await using var ms = new MemoryStream(data);
                secureUrl = await _objectStorage.UploadFileAsync(storageKey, ms, mediaType, cancellationToken);
            }
            else
            {
                if (!Uri.TryCreate(source, UriKind.Absolute, out var imageUri))
                {
                    _logger.LogDebug("Skipping relative image source {Source}", source);
                    continue;
                }

                using var imageResponse = await client.GetAsync(imageUri, cancellationToken);
                if (!imageResponse.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Failed to download image {Uri} (status {Status})", imageUri, imageResponse.StatusCode);
                    continue;
                }

                await using var responseStream = await imageResponse.Content.ReadAsStreamAsync(cancellationToken);
                using var copy = new MemoryStream();
                await responseStream.CopyToAsync(copy, cancellationToken);
                copy.Position = 0;
                var mediaType = imageResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
                secureUrl = await _objectStorage.UploadFileAsync(storageKey, copy, mediaType, cancellationToken);
            }

            results.Add(new LogoAsset
            {
                AssetId = $"google_{index}",
                AssetType = "image",
                BoundingBox = new BoundingBox { X = 0, Y = 0, Width = 120, Height = 120, PageNumber = 1 },
                SecureUrl = secureUrl,
                StorageKey = storageKey
            });
        }

        return results;
    }

    private static (string mediaType, byte[] data) ParseDataUri(string dataUri)
    {
        var commaIndex = dataUri.IndexOf(',');
        if (commaIndex < 0)
        {
            throw new InvalidOperationException("Malformed data URI.");
        }

        var metadata = dataUri.Substring(5, commaIndex - 5); // skip "data:"
        var base64Data = dataUri[(commaIndex + 1)..];
        var mediaType = metadata.Split(';').FirstOrDefault() ?? "image/png";
        var data = Convert.FromBase64String(base64Data);
        return (mediaType, data);
    }

    private static IEnumerable<Section> FlattenSections(IEnumerable<Section> sections)
    {
        foreach (var section in sections)
        {
            yield return section;
            foreach (var child in FlattenSections(section.SubSections ?? Enumerable.Empty<Section>()))
            {
                yield return child;
            }
        }
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private static string NormalizeText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);
        var previousWhitespace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }
}

