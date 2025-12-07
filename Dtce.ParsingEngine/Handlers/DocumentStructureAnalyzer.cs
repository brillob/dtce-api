using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dtce.ParsingEngine.Handlers;

/// <summary>
/// Dynamically analyzes document structure to identify heading patterns based on formatting,
/// styles, and visual hierarchy rather than static keyword lists.
/// </summary>
internal class DocumentStructureAnalyzer
{
    private readonly ILogger _logger;
    private readonly List<ParagraphFeature> _paragraphFeatures = new();
    private readonly Dictionary<Paragraph, ParagraphFeature> _paragraphToFeatureMap = new();
    private HeadingPatternModel? _patternModel;

    public DocumentStructureAnalyzer(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes all paragraphs in the document to build a statistical model of heading patterns.
    /// </summary>
    public void AnalyzeDocument(MainDocumentPart mainPart)
    {
        _paragraphFeatures.Clear();
        _patternModel = null;

        if (mainPart.Document?.Body == null)
        {
            return;
        }

        // Extract features from all paragraphs, preserving document order
        var paragraphIndex = 0;
        foreach (var element in mainPart.Document.Body.Elements())
        {
            if (element is not Paragraph paragraph)
            {
                continue;
            }

            var text = ExtractParagraphText(paragraph);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var features = ExtractParagraphFeatures(paragraph, text);
            features.DocumentIndex = paragraphIndex++; // Preserve document order
            _paragraphFeatures.Add(features);
            _paragraphToFeatureMap[paragraph] = features;
        }

        // Build statistical model to identify heading patterns
        _patternModel = BuildHeadingPatternModel();

        _logger.LogInformation("Analyzed {Count} paragraphs, identified {HeadingCount} potential headings",
            _paragraphFeatures.Count,
            _paragraphFeatures.Count(f => f.IsHeading));
    }

    /// <summary>
    /// Determines if a paragraph is a heading and returns its level (1-6) or 0 if not a heading.
    /// </summary>
    public int DetermineHeadingLevel(Paragraph paragraph, string text, int currentStackLevel)
    {
        if (_patternModel == null)
        {
            // Fallback to basic heuristics if analysis hasn't been performed
            return DetermineHeadingLevelFallback(paragraph, text, currentStackLevel);
        }

        // Try to find the analyzed feature for this paragraph
        ParagraphFeature? features = null;
        if (_paragraphToFeatureMap.TryGetValue(paragraph, out var analyzedFeature))
        {
            features = analyzedFeature;
        }
        else
        {
            // If not found, extract features on the fly
            features = ExtractParagraphFeatures(paragraph, text);
            // Recalculate score if needed
            if (_patternModel != null)
            {
                features.HeadingScore = CalculateHeadingScore(features, _patternModel.AverageBodyFontSize, _patternModel.AverageBodyWordCount);
            }
        }
        
        // Use the statistical model to classify
        if (!IsLikelyHeading(features))
        {
            return 0;
        }

        // Determine heading level based on formatting hierarchy
        return DetermineHeadingLevelFromFeatures(features, currentStackLevel);
    }

    private ParagraphFeature ExtractParagraphFeatures(Paragraph paragraph, string text)
    {
        var feature = new ParagraphFeature
        {
            Text = text,
            TextLength = text.Length,
            WordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
            HasColon = text.TrimEnd().EndsWith(":", StringComparison.Ordinal),
            IsNumbered = NumberedHeadingRegex.IsMatch(text),
            IsBulleted = BulletedHeadingRegex.IsMatch(text),
            UppercaseRatio = CalculateUppercaseRatio(text)
        };

        // Extract style information
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        feature.StyleId = styleId;
        feature.IsHeadingStyle = !string.IsNullOrEmpty(styleId) &&
            (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) ||
             styleId.StartsWith("Title", StringComparison.OrdinalIgnoreCase));

        if (feature.IsHeadingStyle && styleId != null)
        {
            var digits = new string(styleId.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out var level))
            {
                feature.StyleLevel = Math.Clamp(level, 1, 6);
            }
            else if (styleId.StartsWith("Title", StringComparison.OrdinalIgnoreCase))
            {
                feature.StyleLevel = 1;
            }
        }

        // Extract formatting from runs
        var firstRun = paragraph.Elements<Run>().FirstOrDefault();
        if (firstRun?.RunProperties != null)
        {
            var runProps = firstRun.RunProperties;
            feature.IsBold = runProps.Bold != null;
            feature.IsItalic = runProps.Italic != null;
            feature.IsUnderline = runProps.Underline != null;

            // Font size (in half-points)
            if (runProps.FontSize?.Val != null)
            {
                if (int.TryParse(runProps.FontSize.Val.Value, out var fontSize))
                {
                    feature.FontSize = fontSize;
                    feature.FontSizePoints = fontSize / 2.0;
                }
            }

            // Font family
            feature.FontFamily = runProps.RunFonts?.Ascii?.Value ?? "Calibri";

            // Color
            if (runProps.Color?.Val != null)
            {
                feature.Color = runProps.Color.Val.Value;
            }
        }

        // Extract paragraph properties
        var paraProps = paragraph.ParagraphProperties;
        if (paraProps != null)
        {
            // Indentation (stored as string in OpenXML, parse if needed)
            if (paraProps.Indentation?.Left != null)
            {
                var leftValueStr = paraProps.Indentation.Left.Value;
                if (long.TryParse(leftValueStr, out var leftValue))
                {
                    feature.LeftIndentation = leftValue;
                }
            }

            // Spacing
            if (paraProps.SpacingBetweenLines != null)
            {
                if (paraProps.SpacingBetweenLines.Before != null)
                {
                    feature.SpaceBefore = paraProps.SpacingBetweenLines.Before.Value;
                }
                if (paraProps.SpacingBetweenLines.After != null)
                {
                    feature.SpaceAfter = paraProps.SpacingBetweenLines.After.Value;
                }
            }
        }

        return feature;
    }

    private HeadingPatternModel BuildHeadingPatternModel()
    {
        if (_paragraphFeatures.Count == 0)
        {
            return new HeadingPatternModel();
        }

        // Calculate statistics for body text (non-headings)
        var bodyTextFeatures = _paragraphFeatures.Where(f => !f.IsHeadingStyle && !f.IsBold).ToList();
        var avgBodyFontSize = bodyTextFeatures.Any() 
            ? bodyTextFeatures.Average(f => f.FontSizePoints) 
            : 11.0; // Default body text size
        var avgBodyWordCount = bodyTextFeatures.Any()
            ? bodyTextFeatures.Average(f => f.WordCount)
            : 20.0;

        // Identify heading patterns based on statistical analysis
        var headingFeatures = new List<ParagraphFeature>();
        
        foreach (var feature in _paragraphFeatures)
        {
            var headingScore = CalculateHeadingScore(feature, avgBodyFontSize, avgBodyWordCount);
            feature.HeadingScore = headingScore;
            
            // Lower threshold to catch more headings, especially those with style information
            var threshold = feature.IsHeadingStyle ? 0.3 : 0.4;
            if (headingScore > threshold)
            {
                feature.IsHeading = true;
                headingFeatures.Add(feature);
            }
        }

        // Cluster headings by formatting to determine levels
        var headingLevels = ClusterHeadingsByFormatting(headingFeatures, avgBodyFontSize);

        return new HeadingPatternModel
        {
            AverageBodyFontSize = avgBodyFontSize,
            AverageBodyWordCount = avgBodyWordCount,
            HeadingFeatures = headingFeatures,
            HeadingLevels = headingLevels
        };
    }

    private double CalculateHeadingScore(ParagraphFeature feature, double avgBodyFontSize, double avgBodyWordCount)
    {
        var score = 0.0;

        // Style-based indicators (strongest signal)
        if (feature.IsHeadingStyle)
        {
            score += 0.4;
        }

        // Font size relative to body text
        if (feature.FontSizePoints > avgBodyFontSize * 1.1) // 10% larger than body
        {
            score += 0.3;
        }
        else if (feature.FontSizePoints < avgBodyFontSize * 0.9) // Smaller than body
        {
            score -= 0.2; // Likely not a heading
        }

        // Bold formatting
        if (feature.IsBold)
        {
            score += 0.15;
        }

        // Text length (headings are typically shorter)
        if (feature.WordCount <= 15 && feature.WordCount > 0)
        {
            score += 0.1;
        }
        else if (feature.WordCount > 30)
        {
            score -= 0.2; // Likely body text
        }

        // Ends with colon (common heading pattern)
        if (feature.HasColon)
        {
            score += 0.1;
        }

        // Numbered headings
        if (feature.IsNumbered)
        {
            score += 0.1;
        }

        // Uppercase ratio (some headings are all caps)
        if (feature.UppercaseRatio > 0.6 && feature.WordCount <= 10)
        {
            score += 0.1;
        }

        // Negative indicators (body text patterns)
        if (feature.IsBulleted)
        {
            score -= 0.3; // Bullets are usually content, not headings
        }

        // Multiple sentences indicate body text
        var sentenceCount = feature.Text.Count(c => c == '.' || c == '!' || c == '?');
        if (sentenceCount >= 2)
        {
            score -= 0.2;
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    private Dictionary<ParagraphFeature, int> ClusterHeadingsByFormatting(
        List<ParagraphFeature> headings, 
        double avgBodyFontSize)
    {
        var levelMap = new Dictionary<ParagraphFeature, int>();

        if (!headings.Any())
        {
            return levelMap;
        }

        // First, use style levels if available (most reliable)
        foreach (var feature in headings.Where(f => f.StyleLevel > 0))
        {
            levelMap[feature] = feature.StyleLevel;
        }

        // For headings without explicit style levels, use document order and formatting
        var headingsWithoutLevel = headings.Where(h => !levelMap.ContainsKey(h)).ToList();
        if (!headingsWithoutLevel.Any())
        {
            return levelMap;
        }

        // Group by formatting characteristics (font size, bold, indentation)
        var formattingGroups = GroupHeadingsByFormatting(headingsWithoutLevel, avgBodyFontSize);
        
        // Assign levels based on formatting hierarchy and document order
        var assignedLevels = AssignLevelsByHierarchy(formattingGroups, headingsWithoutLevel, avgBodyFontSize);
        
        foreach (var kvp in assignedLevels)
        {
            levelMap[kvp.Key] = kvp.Value;
        }

        return levelMap;
    }

    private List<List<ParagraphFeature>> GroupHeadingsByFormatting(
        List<ParagraphFeature> headings, 
        double avgBodyFontSize)
    {
        var groups = new List<List<ParagraphFeature>>();
        
        // Sort by font size (descending) and then by other formatting characteristics
        var sorted = headings.OrderByDescending(h => h.FontSizePoints)
            .ThenByDescending(h => h.IsBold ? 1 : 0)
            .ThenByDescending(h => h.LeftIndentation ?? 0)
            .ToList();

        var fontSizeThreshold = avgBodyFontSize * 0.3; // Smaller threshold for better grouping
        var currentGroup = new List<ParagraphFeature> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = currentGroup[0];
            var curr = sorted[i];
            
            var fontSizeDiff = Math.Abs(prev.FontSizePoints - curr.FontSizePoints);
            var boldMatch = prev.IsBold == curr.IsBold;
            var indentDiff = Math.Abs((prev.LeftIndentation ?? 0) - (curr.LeftIndentation ?? 0));
            
            // Group if formatting is similar
            if (fontSizeDiff < fontSizeThreshold && boldMatch && indentDiff < 100)
            {
                currentGroup.Add(curr);
            }
            else
            {
                groups.Add(currentGroup);
                currentGroup = new List<ParagraphFeature> { curr };
            }
        }
        groups.Add(currentGroup);

        return groups;
    }

    private Dictionary<ParagraphFeature, int> AssignLevelsByHierarchy(
        List<List<ParagraphFeature>> formattingGroups,
        List<ParagraphFeature> allHeadings,
        double avgBodyFontSize)
    {
        var levelMap = new Dictionary<ParagraphFeature, int>();
        
        // Use DocumentIndex for ordering (preserves document order)
        var sortedByOrder = allHeadings.OrderBy(h => h.DocumentIndex).ToList();

        // Assign base levels to formatting groups (larger fonts = lower level numbers)
        var groupLevels = new Dictionary<List<ParagraphFeature>, int>();
        for (int i = 0; i < formattingGroups.Count && i < 6; i++)
        {
            groupLevels[formattingGroups[i]] = i + 1;
        }

        // Process headings in document order to establish hierarchy
        var currentLevelStack = new Stack<int>();
        currentLevelStack.Push(1); // Root level

        foreach (var feature in sortedByOrder)
        {
            // Find which formatting group this feature belongs to
            var group = formattingGroups.FirstOrDefault(g => g.Contains(feature));
            if (group == null)
            {
                // Assign default level based on formatting
                var relativeSize = feature.FontSizePoints / avgBodyFontSize;
                var defaultLevel = relativeSize switch
                {
                    >= 1.5 => 1,
                    >= 1.3 => 2,
                    >= 1.1 => 3,
                    >= 1.0 => 4,
                    _ => 5
                };
                levelMap[feature] = Math.Clamp(defaultLevel, 1, 6);
                continue;
            }

            var baseLevel = groupLevels.GetValueOrDefault(group, 1);
            
            // Adjust level based on document hierarchy
            // Pop stack until we find a level that's less than baseLevel
            while (currentLevelStack.Count > 0 && currentLevelStack.Peek() >= baseLevel)
            {
                currentLevelStack.Pop();
            }

            // Determine final level: should be at most one level deeper than current stack top
            var finalLevel = currentLevelStack.Count > 0 
                ? Math.Min(baseLevel, currentLevelStack.Peek() + 1)
                : baseLevel;
            
            finalLevel = Math.Clamp(finalLevel, 1, 6);
            levelMap[feature] = finalLevel;
            currentLevelStack.Push(finalLevel);
        }

        // Detect recurring patterns (same formatting appearing multiple times)
        DetectAndAdjustRecurringPatterns(levelMap, sortedByOrder, formattingGroups);

        return levelMap;
    }

    private void DetectAndAdjustRecurringPatterns(
        Dictionary<ParagraphFeature, int> levelMap,
        List<ParagraphFeature> sortedHeadings,
        List<List<ParagraphFeature>> formattingGroups)
    {
        // Identify recurring patterns: same formatting appearing multiple times
        foreach (var group in formattingGroups)
        {
            if (group.Count < 2)
            {
                continue; // Need at least 2 occurrences to be a pattern
            }

            // Check if these headings appear in a recurring structure
            var groupHeadings = group.OrderBy(h => h.DocumentIndex).ToList();
            
            // If headings in this group appear at regular intervals or in similar contexts,
            // they likely represent the same hierarchical level
            var commonLevel = groupHeadings
                .Where(h => levelMap.ContainsKey(h))
                .Select(h => levelMap[h])
                .GroupBy(l => l)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            if (commonLevel > 0)
            {
                // Ensure all headings in this pattern have the same level
                foreach (var heading in groupHeadings)
                {
                    if (levelMap.ContainsKey(heading))
                    {
                        levelMap[heading] = commonLevel;
                    }
                }
            }
        }

        // Adjust levels based on document structure (ensure proper hierarchy)
        var adjustedLevels = new Dictionary<ParagraphFeature, int>(levelMap);
        for (int i = 1; i < sortedHeadings.Count; i++)
        {
            var prev = sortedHeadings[i - 1];
            var curr = sortedHeadings[i];
            
            if (!adjustedLevels.ContainsKey(prev) || !adjustedLevels.ContainsKey(curr))
            {
                continue;
            }

            var prevLevel = adjustedLevels[prev];
            var currLevel = adjustedLevels[curr];

            // Ensure proper hierarchy: levels should not jump more than 1 level
            if (currLevel > prevLevel + 1)
            {
                // Can't jump more than one level - make it a subsection of previous
                adjustedLevels[curr] = prevLevel + 1;
            }
            // If current is smaller level number than previous, it's a higher-level heading (correct)
            else if (currLevel < prevLevel)
            {
                // This is correct - it's a higher-level heading, possibly closing previous sections
            }
            // If same level, they're siblings (correct)
            else if (currLevel == prevLevel)
            {
                // Same level headings are siblings - this is correct
                // But check if formatting suggests one should be a subsection
                var fontSizeDiff = Math.Abs(prev.FontSizePoints - curr.FontSizePoints);
                var indentDiff = Math.Abs((prev.LeftIndentation ?? 0) - (curr.LeftIndentation ?? 0));
                
                // If significant formatting difference, consider making one a subsection
                if (fontSizeDiff > 2.0 || indentDiff > 200)
                {
                    // The one with smaller font or more indentation should be a subsection
                    if (curr.FontSizePoints < prev.FontSizePoints - 1.0 || 
                        (curr.LeftIndentation ?? 0) > (prev.LeftIndentation ?? 0) + 100)
                    {
                        adjustedLevels[curr] = Math.Min(prevLevel + 1, 6);
                    }
                }
            }
        }

        // Update levelMap with adjusted levels
        foreach (var kvp in adjustedLevels)
        {
            levelMap[kvp.Key] = kvp.Value;
        }
    }

    private bool IsLikelyHeading(ParagraphFeature feature)
    {
        if (_patternModel == null)
        {
            return feature.IsHeadingStyle || (feature.IsBold && feature.FontSizePoints > 12);
        }

        // Use the statistical model with adjusted threshold
        var threshold = feature.IsHeadingStyle ? 0.3 : 0.4;
        return feature.HeadingScore > threshold || feature.IsHeading;
    }

    private int DetermineHeadingLevelFromFeatures(ParagraphFeature feature, int currentStackLevel)
    {
        if (_patternModel == null)
        {
            return DetermineHeadingLevelFallback(null!, feature.Text, currentStackLevel);
        }

        // Use style level if available (most reliable)
        if (feature.StyleLevel > 0)
        {
            return feature.StyleLevel;
        }

        // Use clustered level from pattern model
        if (_patternModel.HeadingLevels.TryGetValue(feature, out var clusteredLevel))
        {
            return clusteredLevel;
        }

        // Fallback to relative sizing
        var relativeSize = feature.FontSizePoints / _patternModel.AverageBodyFontSize;
        return relativeSize switch
        {
            >= 1.5 => 1,
            >= 1.3 => 2,
            >= 1.1 => 3,
            >= 1.0 => 4,
            _ => Math.Clamp(currentStackLevel + 1, 1, 6)
        };
    }

    private int DetermineHeadingLevelFallback(Paragraph paragraph, string text, int currentStackLevel)
    {
        // Basic fallback heuristics when analysis hasn't been performed
        if (BulletedHeadingRegex.IsMatch(text))
        {
            return 0;
        }

        if (IsLikelyBodyText(text))
        {
            return 0;
        }

        var normalized = NormalizeHeadingText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        if (TryGetNumberedHeadingLevel(normalized, out var numberedLevel))
        {
            return numberedLevel;
        }

        if (paragraph != null && HasHeadingFormatting(paragraph))
        {
            return currentStackLevel == 0 ? 1 : currentStackLevel;
        }

        if (IsUppercaseHeading(normalized))
        {
            return currentStackLevel == 0 ? 1 : currentStackLevel;
        }

        var endedWithColon = text.TrimEnd().EndsWith(":", StringComparison.Ordinal);
        if (endedWithColon || LooksLikeStandaloneHeading(normalized))
        {
            return Math.Clamp(currentStackLevel + 1, 1, 6);
        }

        return 0;
    }

    private static string ExtractParagraphText(Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var run in paragraph.Elements<Run>())
        {
            foreach (var textElement in run.Elements<Text>())
            {
                text.Append(textElement.Text);
            }
        }
        return text.ToString();
    }

    private static double CalculateUppercaseRatio(string text)
    {
        var uppercase = 0;
        var letters = 0;

        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                letters++;
                if (char.IsUpper(ch))
                {
                    uppercase++;
                }
            }
        }

        return letters == 0 ? 0 : (double)uppercase / letters;
    }

    private static bool HasHeadingFormatting(Paragraph paragraph)
    {
        var runProperties = paragraph.Elements<Run>().FirstOrDefault()?.RunProperties;
        if (runProperties == null) return false;

        var isBold = runProperties.Bold != null;
        var fontSize = 0;
        if (runProperties.FontSize?.Val != null)
        {
            var fontSizeValue = runProperties.FontSize.Val.Value;
            if (int.TryParse(fontSizeValue, out var parsedSize))
            {
                fontSize = parsedSize;
            }
        }
        var isLargerFont = fontSize > 220; // 11pt = 220 half-points

        return isBold && isLargerFont;
    }

    private static bool IsUppercaseHeading(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12 &&
        CalculateUppercaseRatio(text) >= 0.6;

    private static bool LooksLikeStandaloneHeading(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 14)
        {
            return false;
        }

        if (text.Contains('.', StringComparison.Ordinal) || text.Contains(';', StringComparison.Ordinal) ||
            text.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        return char.IsLetterOrDigit(text.FirstOrDefault());
    }

    private static bool IsLikelyBodyText(string text)
    {
        var trimmed = text.Trim();
        var wordCount = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (wordCount > 30)
        {
            return true;
        }

        if (trimmed.Count(c => c == '.') >= 2)
        {
            return true;
        }

        if (trimmed.Contains(". ") && trimmed.Length > 80)
        {
            return true;
        }

        return false;
    }

    private static bool TryGetNumberedHeadingLevel(string text, out int level)
    {
        level = 0;
        var match = NumberedHeadingRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var token = match.Groups["token"].Value.Trim().TrimEnd('.', ')');
        if (string.IsNullOrWhiteSpace(token))
        {
            level = 1;
            return true;
        }

        if (token.Contains('.', StringComparison.Ordinal))
        {
            var segments = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            level = Math.Clamp(segments.Length, 1, 6);
            return true;
        }

        if (int.TryParse(token, out _))
        {
            level = 1;
            return true;
        }

        if (token.Length == 1 && char.IsLetter(token[0]))
        {
            level = 2;
            return true;
        }

        level = 1;
        return true;
    }

    private static string NormalizeHeadingText(string text) =>
        text.Trim().TrimEnd(':', '-', '–').Trim();

    private static readonly System.Text.RegularExpressions.Regex NumberedHeadingRegex = 
        new(@"^(?<token>(\d+(\.\d+)*|[A-Z]\)|[IVXLC]+\.)\s+)", System.Text.RegularExpressions.RegexOptions.Compiled);
    
    private static readonly System.Text.RegularExpressions.Regex BulletedHeadingRegex = 
        new(@"^(\-|\*|•)\s+\S+", System.Text.RegularExpressions.RegexOptions.Compiled);
}

internal class ParagraphFeature
{
    public string Text { get; set; } = string.Empty;
    public int TextLength { get; set; }
    public int WordCount { get; set; }
    public bool HasColon { get; set; }
    public bool IsNumbered { get; set; }
    public bool IsBulleted { get; set; }
    public double UppercaseRatio { get; set; }
    public string? StyleId { get; set; }
    public bool IsHeadingStyle { get; set; }
    public int StyleLevel { get; set; }
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public bool IsUnderline { get; set; }
    public int FontSize { get; set; }
    public double FontSizePoints { get; set; }
    public string FontFamily { get; set; } = "Calibri";
    public string? Color { get; set; }
    public long? LeftIndentation { get; set; }
    public string? SpaceBefore { get; set; }
    public string? SpaceAfter { get; set; }
    public double HeadingScore { get; set; }
    public bool IsHeading { get; set; }
    public int DocumentIndex { get; set; } // Position in document for hierarchy detection
}

internal class HeadingPatternModel
{
    public double AverageBodyFontSize { get; set; }
    public double AverageBodyWordCount { get; set; }
    public List<ParagraphFeature> HeadingFeatures { get; set; } = new();
    public Dictionary<ParagraphFeature, int> HeadingLevels { get; set; } = new();
}

