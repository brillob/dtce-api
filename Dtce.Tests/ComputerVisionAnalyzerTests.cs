using Dtce.AnalysisEngine;
using Dtce.AnalysisEngine.Services;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;

namespace Dtce.Tests;

public class ComputerVisionAnalyzerTests
{
    private readonly TestObjectStorage _storage = new();
    private readonly ComputerVisionAnalyzer _analyzer;

    public ComputerVisionAnalyzerTests()
    {
        _analyzer = new ComputerVisionAnalyzer(_storage, NullLogger<ComputerVisionAnalyzer>.Instance);
    }

    [Fact]
    public async Task DetectLogosAsync_WhenGivenHighContrastImage_FlagsLogo()
    {
        var imageBytes = CreateLogoCandidate();
        const string storageKey = "images/logo.png";
        _storage.SeedFile(storageKey, imageBytes, "image/png");

        var parseResult = new ParseResult
        {
            TemplateJson = new TemplateJson
            {
                LogoMap = new List<LogoAsset>
                {
                    new()
                    {
                        AssetId = "logo-1",
                        AssetType = "image",
                        StorageKey = storageKey,
                        BoundingBox = new BoundingBox { X = 0, Y = 0, Width = 64, Height = 64, PageNumber = 1 }
                    }
                }
            }
        };

        var result = await _analyzer.DetectLogosAsync(parseResult);

        result.Should().HaveCount(1);
        result[0].AssetType.Should().Be("logo");
        result[0].SecureUrl.Should().StartWith("https://local.test");
    }

    private static byte[] CreateLogoCandidate()
    {
        using var image = new Image<Rgba32>(64, 64, new Rgba32(255, 255, 255, 0));
        for (var y = 16; y < 48; y++)
        {
            for (var x = 16; x < 48; x++)
            {
                image[x, y] = new Rgba32(10, 10, 10, 255);
            }
        }

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }
}


