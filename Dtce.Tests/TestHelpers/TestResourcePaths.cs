namespace Dtce.Tests.TestHelpers;

internal static class TestResourcePaths
{
    private static readonly Lazy<string> _sampleDocsRoot = new(ResolveSampleDocsRoot);

    public static string SampleDocsRoot => _sampleDocsRoot.Value;

    public static string GetSampleDocument(string fileName) =>
        Path.Combine(SampleDocsRoot, fileName);

    private static string ResolveSampleDocsRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var solutionPath = Path.Combine(current.FullName, "DtceApi.sln");
            var candidate = Path.Combine(current.FullName, "SampleDocs");
            if (File.Exists(solutionPath) && Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "SampleDocs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Failed to locate the SampleDocs directory from the current test context.");
    }
}


