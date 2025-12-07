using System.Net;
using System.Net.Http;
using System.Text;
using Dtce.Common;
using Dtce.ParsingEngine.Handlers;
using Dtce.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp.Formats.Png;

namespace Dtce.Tests;

public class GoogleDocsHandlerTests
{
    private readonly TestObjectStorage _objectStorage = new();

    [Fact]
    public async Task ParseAsync_DownloadsHtmlAndExtractsSectionsAndImages()
    {
        var pngBytes = CreateTestPng();
        var dataUri = "data:image/png;base64," + Convert.ToBase64String(pngBytes);
        var html = $"""
            <html>
                <body>
                    <h1>Executive Summary</h1>
                    <p>This document highlights the accomplishments of the engineering organization.</p>
                    <h2>Highlights</h2>
                    <p>Delivered multiple major releases ahead of schedule.</p>
                    <img src="{dataUri}" />
                </body>
            </html>
            """;

        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://docs.google.com/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(nameof(GoogleDocsHandler))).Returns(httpClient);

        var googleHandler = new GoogleDocsHandler(
            _objectStorage,
            factory.Object,
            NullLogger<GoogleDocsHandler>.Instance);

        var jobRequest = new JobRequest
        {
            JobId = "google-job",
            DocumentType = DocumentType.GoogleDoc,
            DocumentUrl = "https://docs.google.com/document/d/123456789/edit"
        };

        var result = await googleHandler.ParseAsync(jobRequest);

        result.TemplateJson.SectionHierarchy.Sections.Should().HaveCount(1);
        result.TemplateJson.SectionHierarchy.Sections[0].SubSections.Should().HaveCount(1);
        result.ContentSections.Should().NotBeEmpty();
        result.TemplateJson.LogoMap.Should().HaveCount(1);

        var logo = result.TemplateJson.LogoMap.Single();
        logo.StorageKey.Should().NotBeNullOrWhiteSpace();
        logo.SecureUrl.Should().StartWith("https://local.test");

        _objectStorage.UploadedKeys.Should().Contain(logo.StorageKey);
    }

    private static byte[] CreateTestPng()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(32, 32);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var color = x < image.Width / 2
                    ? new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 255, 0)
                    : new SixLabors.ImageSharp.PixelFormats.Rgba32(20, 20, 20, 255);
                image[x, y] = color;
            }
        }

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}


