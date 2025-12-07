using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;

namespace Tools.ValidateDocx;

class Program
{
    static void Main(string[] args)
    {
        var docPath = Path.Combine("SampleDocs", "test1", "generated-resume.docx");
        
        if (!File.Exists(docPath))
        {
            Console.WriteLine($"File not found: {docPath}");
            return;
        }

        Console.WriteLine($"Validating document: {docPath}");
        
        try
        {
            using (var document = WordprocessingDocument.Open(docPath, false))
            {
                var validator = new OpenXmlValidator();
                var errors = validator.Validate(document).ToList();
                
                if (errors.Any())
                {
                    Console.WriteLine($"\nFound {errors.Count} validation errors:");
                    foreach (var error in errors.Take(20))
                    {
                        Console.WriteLine($"  - {error.ErrorType}: {error.Description}");
                        Console.WriteLine($"    Path: {error.Path?.XPath}");
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine("✓ Document structure is valid!");
                }
                
                // Check document structure
                var mainPart = document.MainDocumentPart;
                if (mainPart?.Document?.Body != null)
                {
                    var paragraphs = mainPart.Document.Body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().Count();
                    var sections = mainPart.Document.Body.Elements<DocumentFormat.OpenXml.Wordprocessing.SectionProperties>().Count();
                    Console.WriteLine($"\nDocument structure:");
                    Console.WriteLine($"  - Paragraphs: {paragraphs}");
                    Console.WriteLine($"  - SectionProperties: {sections}");
                    Console.WriteLine($"  - Total elements in body: {mainPart.Document.Body.Elements().Count()}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ Error opening document: {ex.Message}");
            Console.WriteLine($"  {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"  Inner: {ex.InnerException.Message}");
            }
        }
    }
}

