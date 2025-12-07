using Dtce.Common;
using Dtce.Common.Models;

namespace Dtce.AnalysisEngine;

public interface IComputerVisionAnalyzer
{
    Task<List<LogoAsset>> DetectLogosAsync(ParseResult parseResult, CancellationToken cancellationToken = default);
}

