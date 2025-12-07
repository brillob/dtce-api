using Dtce.Common;

namespace Dtce.ParsingEngine.Handlers;

public interface IDocumentHandler
{
    Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken = default);
}

