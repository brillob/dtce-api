using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Dtce.AnalysisEngine.Services;

public class ComputerVisionAnalyzer : IComputerVisionAnalyzer
{
    private readonly IObjectStorage _objectStorage;
    private readonly ILogger<ComputerVisionAnalyzer> _logger;

    public ComputerVisionAnalyzer(
        IObjectStorage objectStorage,
        ILogger<ComputerVisionAnalyzer> logger)
    {
        _objectStorage = objectStorage;
        _logger = logger;
    }

    public async Task<List<LogoAsset>> DetectLogosAsync(ParseResult parseResult, CancellationToken cancellationToken = default)
    {
        if (parseResult.TemplateJson?.LogoMap == null || parseResult.TemplateJson.LogoMap.Count == 0)
        {
            _logger.LogInformation("No candidate images discovered in parse result.");
            return new List<LogoAsset>();
        }

        var results = new List<LogoAsset>();

        foreach (var asset in parseResult.TemplateJson.LogoMap)
        {
            if (string.IsNullOrWhiteSpace(asset.StorageKey))
            {
                _logger.LogDebug("Skipping asset {AssetId} because no storage key was provided.", asset.AssetId);
                continue;
            }

            try
            {
                await using var stream = await _objectStorage.DownloadFileAsync(asset.StorageKey, cancellationToken);
                using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken);

                var features = AnalyzeImage(image);
                var secureUrl = await _objectStorage.GeneratePreSignedUrlAsync(asset.StorageKey, TimeSpan.FromHours(12), cancellationToken);

                results.Add(new LogoAsset
                {
                    AssetId = asset.AssetId,
                    AssetType = features.IsLikelyLogo ? "logo" : string.IsNullOrEmpty(asset.AssetType) ? "image" : asset.AssetType,
                    BoundingBox = asset.BoundingBox,
                    SecureUrl = secureUrl,
                    StorageKey = asset.StorageKey
                });

                _logger.LogDebug("Analyzed asset {AssetId}: LogoCandidate={IsLogo}, ColorVariance={Variance:F3}, Transparency={Transparency:P0}",
                    asset.AssetId, features.IsLikelyLogo, features.ColorDiversity, features.TransparencyRatio);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze asset {AssetId}", asset.AssetId);
            }
        }

        _logger.LogInformation("Logo detection completed: Found {Count} assets.", results.Count);
        return results;
    }

    private static LogoFeatures AnalyzeImage(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;
        var sampleStepX = Math.Max(1, width / 128);
        var sampleStepY = Math.Max(1, height / 128);

        var uniqueColors = new HashSet<uint>();
        var totalSamples = 0;
        var transparentPixels = 0;
        var edgeColorSamples = new Dictionary<uint, int>();

        for (var y = 0; y < height; y += sampleStepY)
        {
            for (var x = 0; x < width; x += sampleStepX)
            {
                var pixel = image[x, y];
                totalSamples++;

                var packed = Pack(pixel);
                uniqueColors.Add(packed);

                if (pixel.A < 80)
                {
                    transparentPixels++;
                }

                if (x == 0 || y == 0 || x >= width - sampleStepX || y >= height - sampleStepY)
                {
                    edgeColorSamples.TryGetValue(packed, out var count);
                    edgeColorSamples[packed] = count + 1;
                }
            }
        }

        var colorDiversity = totalSamples == 0 ? 0 : (double)uniqueColors.Count / totalSamples;
        var transparencyRatio = totalSamples == 0 ? 0 : (double)transparentPixels / totalSamples;
        var dominantEdgeRatio = edgeColorSamples.Values.Count == 0
            ? 0
            : (double)edgeColorSamples.Values.Max() / edgeColorSamples.Values.Sum();

        var isLogo = colorDiversity < 0.18
                     || (transparencyRatio > 0.25 && colorDiversity < 0.35)
                     || (dominantEdgeRatio > 0.4 && colorDiversity < 0.4);

        // Logos are often relatively small assets.
        if (width * height < 40000)
        {
            isLogo = true;
        }

        return new LogoFeatures(isLogo, colorDiversity, transparencyRatio);
    }

    private static uint Pack(Rgba32 pixel) =>
        ((uint)pixel.R << 24) | ((uint)pixel.G << 16) | ((uint)pixel.B << 8) | pixel.A;

    private sealed record LogoFeatures(bool IsLikelyLogo, double ColorDiversity, double TransparencyRatio);
}

