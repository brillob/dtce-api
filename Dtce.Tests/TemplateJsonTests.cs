using Dtce.Common.Models;
using FluentAssertions;
using Xunit;

namespace Dtce.Tests;

public class TemplateJsonTests
{
    [Fact]
    public void TemplateJson_DefaultValues_AreInitialized()
    {
        // Arrange & Act
        var templateJson = new TemplateJson();

        // Assert
        templateJson.VisualTheme.Should().NotBeNull();
        templateJson.SectionHierarchy.Should().NotBeNull();
        templateJson.LogoMap.Should().NotBeNull();
        templateJson.LogoMap.Should().BeEmpty();
    }

    [Fact]
    public void VisualTheme_DefaultValues_AreInitialized()
    {
        // Arrange & Act
        var visualTheme = new VisualTheme();

        // Assert
        visualTheme.ColorPalette.Should().NotBeNull();
        visualTheme.ColorPalette.Should().BeEmpty();
        visualTheme.FontMap.Should().NotBeNull();
        visualTheme.FontMap.Should().BeEmpty();
        visualTheme.LayoutRules.Should().NotBeNull();
    }

    [Fact]
    public void LayoutRules_DefaultValues_AreSet()
    {
        // Arrange & Act
        var layoutRules = new LayoutRules();

        // Assert
        layoutRules.Orientation.Should().Be("portrait");
        layoutRules.Margins.Should().NotBeNull();
    }

    [Fact]
    public void SectionHierarchy_DefaultValues_AreInitialized()
    {
        // Arrange & Act
        var sectionHierarchy = new SectionHierarchy();

        // Assert
        sectionHierarchy.Sections.Should().NotBeNull();
        sectionHierarchy.Sections.Should().BeEmpty();
    }
}


