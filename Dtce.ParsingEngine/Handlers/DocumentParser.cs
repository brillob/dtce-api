using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Dtce.ParsingEngine.Handlers;

public class DocumentParser : IDocumentParser
{
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<DocumentParser> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<DocumentType, Type> _handlerTypes;

    public DocumentParser(
        IObjectStorage objectStorage,
        ILogger<DocumentParser> logger,
        IServiceProvider serviceProvider)
    {
        _objectStorage = objectStorage;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _handlerTypes = new Dictionary<DocumentType, Type>
        {
            { DocumentType.Docx, typeof(DocxHandler) },
            { DocumentType.Pdf, typeof(PdfHandler) },
            { DocumentType.GoogleDoc, typeof(GoogleDocsHandler) }
        };
    }

    private IDocumentHandler GetHandler(DocumentType documentType)
    {
        if (!_handlerTypes.TryGetValue(documentType, out var handlerType))
        {
            throw new NotSupportedException($"Document type {documentType} is not supported");
        }

        // Get all registered IDocumentHandler services and find the one matching the requested type
        var handlers = _serviceProvider.GetServices<IDocumentHandler>();
        var handler = handlers.FirstOrDefault(h => h.GetType() == handlerType);
        
        if (handler == null)
        {
            throw new InvalidOperationException($"Handler for {documentType} could not be resolved. Registered handlers: {string.Join(", ", handlers.Select(h => h.GetType().Name))}");
        }

        return handler;
    }

    public async Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken = default)
    {
        var handler = GetHandler(jobRequest.DocumentType);
        _logger.LogInformation("Parsing {DocumentType} document for job {JobId}", jobRequest.DocumentType, jobRequest.JobId);
        return await handler.ParseAsync(jobRequest, cancellationToken);
    }
}

