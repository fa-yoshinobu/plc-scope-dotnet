namespace PlcScope.App.UiTests;

using System.Xml.Linq;

public sealed class MonitorLayoutTests
{
    [Theory]
    [InlineData("PackedBitRowTemplate")]
    [InlineData("SingleBitRowTemplate")]
    public void BitRowTemplates_AlignCommentWithMonitorHeader(string templateKey)
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document
            .Descendants(presentation + "DataTemplate")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), templateKey, StringComparison.Ordinal));
        var grid = template.Element(presentation + "Grid")!;
        var columnCount = grid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Count();
        var commentColumn = grid
            .Elements(presentation + "TextBlock")
            .Single(element => string.Equals((string?)element.Attribute("Text"), "{Binding Comment}", StringComparison.Ordinal))
            .Attribute("Grid.Column")!
            .Value;

        Assert.Equal(5, columnCount);
        Assert.Equal("4", commentColumn);
    }

    private static string ResolveRepoPath(params string[] relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePath).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(relativePath)} from {AppContext.BaseDirectory}.");
    }
}
