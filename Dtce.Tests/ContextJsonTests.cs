using Dtce.Common.Models;
using FluentAssertions;
using Xunit;

namespace Dtce.Tests;

public class ContextJsonTests
{
    [Fact]
    public void ContextJson_DefaultValues_AreInitialized()
    {
        // Arrange & Act
        var contextJson = new ContextJson();

        // Assert
        contextJson.LinguisticStyle.Should().NotBeNull();
        contextJson.ContentBlocks.Should().NotBeNull();
        contextJson.ContentBlocks.Should().BeEmpty();
    }

    [Fact]
    public void LinguisticStyleAttributes_DefaultValues_AreSet()
    {
        // Arrange & Act
        var linguisticStyle = new LinguisticStyleAttributes();

        // Assert
        linguisticStyle.OverallFormality.Should().BeEmpty();
        linguisticStyle.FormalityConfidenceScore.Should().Be(0.0);
        linguisticStyle.DominantTone.Should().BeEmpty();
        linguisticStyle.ToneConfidenceScore.Should().Be(0.0);
        linguisticStyle.WritingStyleVector.Should().NotBeNull();
        linguisticStyle.WritingStyleVector.Should().BeEmpty();
    }

    [Fact]
    public void ContentBlock_Properties_CanBeSet()
    {
        // Arrange
        var placeholderId = "placeholder_test";
        var sampleText = "This is sample text";
        var wordCount = 4;

        // Act
        var contentBlock = new ContentBlock
        {
            PlaceholderId = placeholderId,
            SectionSampleText = sampleText,
            WordCount = wordCount
        };

        // Assert
        contentBlock.PlaceholderId.Should().Be(placeholderId);
        contentBlock.SectionSampleText.Should().Be(sampleText);
        contentBlock.WordCount.Should().Be(wordCount);
    }
}


