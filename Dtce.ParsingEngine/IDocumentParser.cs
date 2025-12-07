using Dtce.Common;

namespace Dtce.ParsingEngine;

public interface IDocumentParser
{
    Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken = default);
}

