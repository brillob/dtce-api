using Dtce.Common;
using FluentAssertions;
using Xunit;

namespace Dtce.Tests;

public class JobRequestTests
{
    [Fact]
    public void JobRequest_DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var before = DateTime.UtcNow;
        var jobRequest = new JobRequest();
        var after = DateTime.UtcNow;

        // Assert
        jobRequest.JobId.Should().BeEmpty();
        jobRequest.CreatedAt.Should().BeOnOrAfter(before);
        jobRequest.CreatedAt.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void JobRequest_WithValidData_PropertiesAreSet()
    {
        // Arrange
        var jobId = Guid.NewGuid().ToString();
        var documentType = DocumentType.Docx;
        var fileName = "test.docx";
        var createdAt = DateTime.UtcNow;

        // Act
        var jobRequest = new JobRequest
        {
            JobId = jobId,
            DocumentType = documentType,
            FileName = fileName,
            CreatedAt = createdAt
        };

        // Assert
        jobRequest.JobId.Should().Be(jobId);
        jobRequest.DocumentType.Should().Be(documentType);
        jobRequest.FileName.Should().Be(fileName);
        jobRequest.CreatedAt.Should().Be(createdAt);
    }
}


