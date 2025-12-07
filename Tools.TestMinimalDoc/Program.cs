using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Tools.TestMinimalDoc;

class Program
{
    static void Main(string[] args)
    {
        var outputPath = Path.Combine("SampleDocs", "test1", "minimal-test.docx");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using (var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            mainPart.Document.Body = new Body();

            // Add styles part
            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles();

            // Add DocDefaults
            var docDefaults = new DocDefaults();
            var defaultRunProps = new RunPropertiesDefault();
            var runProps = new RunProperties();
            runProps.Append(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
            runProps.Append(new FontSize { Val = "22" });
            runProps.Append(new FontSizeComplexScript { Val = "22" });
            defaultRunProps.Append(runProps);
            docDefaults.Append(defaultRunProps);
            stylesPart.Styles.Append(docDefaults);

            // Add Normal style
            var normalStyle = new Style
            {
                Type = StyleValues.Paragraph,
                StyleId = "Normal",
                Default = true,
                StyleName = new StyleName { Val = "Normal" },
                UIPriority = new UIPriority { Val = 99 },
                PrimaryStyle = new PrimaryStyle()
            };
            normalStyle.StyleRunProperties = new StyleRunProperties();
            normalStyle.StyleRunProperties.Append(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
            normalStyle.StyleRunProperties.Append(new FontSize { Val = "22" });
            normalStyle.StyleRunProperties.Append(new FontSizeComplexScript { Val = "22" });
            stylesPart.Styles.Append(normalStyle);

            // Add a simple paragraph
            var paragraph = new Paragraph(new Run(new Text("Test Document")));
            paragraph.ParagraphProperties = new ParagraphProperties
            {
                ParagraphStyleId = new ParagraphStyleId { Val = "Normal" }
            };
            mainPart.Document.Body.Append(paragraph);

            // Add SectionProperties as last element
            var sectionProperties = new SectionProperties();
            var pageSize = new PageSize
            {
                Width = (UInt32Value)(210 * 56.69291339), // A4 width in mm
                Height = (UInt32Value)(297 * 56.69291339) // A4 height in mm
            };
            var pageMargin = new PageMargin
            {
                Top = (Int32Value)(25.4 * 56.69291339),
                Bottom = (Int32Value)(25.4 * 56.69291339),
                Left = (UInt32Value)(25.4 * 56.69291339),
                Right = (UInt32Value)(25.4 * 56.69291339)
            };
            sectionProperties.Append(pageSize);
            sectionProperties.Append(pageMargin);
            mainPart.Document.Body.Append(sectionProperties);

            mainPart.Document.Save();
        }

        Console.WriteLine($"Minimal test document created: {outputPath}");
        Console.WriteLine("Try opening this file in Word to verify basic structure works.");
    }
}

