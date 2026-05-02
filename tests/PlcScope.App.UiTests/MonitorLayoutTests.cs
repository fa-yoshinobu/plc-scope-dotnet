namespace PlcScope.App.UiTests;

using System.Xml.Linq;

public sealed class MonitorLayoutTests
{
    [Fact]
    public void WatchList_UsesListBoxWithTemplateRows()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var watchList = document
            .Descendants(presentation + "ListBox")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), "WatchListBox", StringComparison.Ordinal));
        var rowTemplate = watchList
            .Element(presentation + "ListBox.ItemTemplate")!
            .Element(presentation + "DataTemplate")!;
        var grid = rowTemplate.Element(presentation + "Grid")!;
        var columnCount = grid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Count();

        Assert.Equal(7, columnCount);
    }

    [Theory]
    [InlineData("WatchTypeComboBox")]
    [InlineData("WatchFormatComboBox")]
    public void WatchOptionControls_AreComboBoxesInListBoxRows(string automationId)
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var rowTemplate = GetWatchRowTemplate(document, presentation);
        var comboBox = rowTemplate
            .Descendants(presentation + "ComboBox")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, automationId, StringComparison.Ordinal)));

        Assert.NotNull(comboBox);
    }

    [Fact]
    public void WatchComment_IsReadOnlyDisplayText()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var rowTemplate = GetWatchRowTemplate(document, presentation);

        Assert.DoesNotContain(rowTemplate.Descendants(presentation + "TextBox"), element =>
            string.Equals((string?)element.Attribute("Text"), "{Binding Comment, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", StringComparison.Ordinal));
        Assert.NotNull(rowTemplate.Descendants(presentation + "TextBlock").SingleOrDefault(element =>
            string.Equals((string?)element.Attribute("Text"), "{Binding Comment}", StringComparison.Ordinal)));
    }

    [Fact]
    public void WatchValue_IsEditableTextBoxWithFrame()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var rowTemplate = GetWatchRowTemplate(document, presentation);
        var textBox = rowTemplate
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "WatchValueTextBox", StringComparison.Ordinal)));

        Assert.Equal("{DynamicResource AppWriteInputBrush}", (string?)textBox.Attribute("Background"));
        Assert.Equal("{DynamicResource AppAccentBrush}", (string?)textBox.Attribute("BorderBrush"));
        Assert.Equal("{Binding ValueText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}", (string?)textBox.Attribute("Text"));
    }

    [Fact]
    public void WatchList_EnablesDragDropReordering()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var watchList = document
            .Descendants(presentation + "ListBox")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), "WatchListBox", StringComparison.Ordinal));

        Assert.Equal("True", (string?)watchList.Attribute("AllowDrop"));
        Assert.Equal("WatchListBox_PreviewMouseLeftButtonDown", (string?)watchList.Attribute("PreviewMouseLeftButtonDown"));
        Assert.Equal("WatchListBox_PreviewMouseMove", (string?)watchList.Attribute("PreviewMouseMove"));
        Assert.Equal("WatchListBox_DragOver", (string?)watchList.Attribute("DragOver"));
        Assert.Equal("WatchListBox_Drop", (string?)watchList.Attribute("Drop"));
    }

    [Theory]
    [InlineData("ImportWatchListCsvMenuItem", "ImportWatchListCsvMenuItem_Click")]
    [InlineData("ExportWatchListCsvMenuItem", "ExportWatchListCsvMenuItem_Click")]
    public void FileMenu_ContainsWatchListCsvCommands(string automationId, string clickHandler)
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);

        var menuItem = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, automationId, StringComparison.Ordinal)));

        Assert.Equal(clickHandler, (string?)menuItem.Attribute("Click"));
    }

    [Fact]
    public void MonitorAndWatchLists_EnableHorizontalScrolling()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[] { "MonitorListBox", "WatchListBox" })
        {
            var listBox = document
                .Descendants(presentation + "ListBox")
                .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), name, StringComparison.Ordinal));
            var horizontalScroll = listBox
                .Attributes()
                .Single(attribute => string.Equals(attribute.Name.LocalName, "ScrollViewer.HorizontalScrollBarVisibility", StringComparison.Ordinal))
                .Value;
            var horizontalContentAlignment = listBox
                .Element(presentation + "ListBox.ItemContainerStyle")!
                .Element(presentation + "Style")!
                .Elements(presentation + "Setter")
                .Single(element => string.Equals((string?)element.Attribute("Property"), "HorizontalContentAlignment", StringComparison.Ordinal))
                .Attribute("Value")!
                .Value;

            Assert.Equal("Visible", horizontalScroll);
            Assert.Equal("Left", horizontalContentAlignment);
        }
    }

    [Fact]
    public void MonitorAndWatchHeaders_ShareColumnWidthsAndScrollWithRows()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var name in new[] { "MonitorHeaderScrollViewer", "WatchHeaderScrollViewer" })
        {
            var headerScrollViewer = document
                .Descendants(presentation + "ScrollViewer")
                .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), name, StringComparison.Ordinal));

            Assert.Equal("Hidden", (string?)headerScrollViewer.Attribute("HorizontalScrollBarVisibility"));
            Assert.Equal("Disabled", (string?)headerScrollViewer.Attribute("VerticalScrollBarVisibility"));
        }

        var monitorHeaderGroups = GetColumnSharedSizeGroups(GetNamedGrid(document, presentation, xaml, "MonitorHeaderGrid"), presentation);
        var monitorRowGroups = GetColumnSharedSizeGroups(GetTemplateGrid(document, presentation, xaml, "WordRowTemplate"), presentation);
        var watchHeaderGroups = GetColumnSharedSizeGroups(GetNamedGrid(document, presentation, xaml, "WatchHeaderGrid"), presentation);
        var watchRowGroups = GetColumnSharedSizeGroups(GetWatchRowTemplate(document, presentation).Element(presentation + "Grid")!, presentation);

        Assert.Equal(monitorHeaderGroups, monitorRowGroups);
        Assert.Equal(watchHeaderGroups, watchRowGroups);
    }

    [Fact]
    public void BitPanels_AreSingleHorizontalLineInMonitorAndWatchRows()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Empty(document.Descendants(presentation + "WrapPanel"));

        var bitItemsControls = document
            .Descendants(presentation + "ItemsControl")
            .Where(element => string.Equals((string?)element.Attribute("ItemsSource"), "{Binding Bits}", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(6, bitItemsControls.Count);
        Assert.All(bitItemsControls, itemsControl =>
        {
            var stackPanel = itemsControl
                .Element(presentation + "ItemsControl.ItemsPanel")!
                .Element(presentation + "ItemsPanelTemplate")!
                .Element(presentation + "StackPanel")!;

            Assert.Equal("Horizontal", (string?)stackPanel.Attribute("Orientation"));
        });
    }

    [Fact]
    public void BitColumns_CanExpandForHorizontalScroll()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var templateKey in new[] { "WordRowTemplate", "PackedBitRowTemplate", "SingleBitRowTemplate", "DWordRowTemplate", "FloatRowTemplate", "ExpandedHeaderTemplate" })
        {
            var template = document
                .Descendants(presentation + "DataTemplate")
                .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), templateKey, StringComparison.Ordinal));
            var bitColumn = template
                .Element(presentation + "Grid")!
                .Element(presentation + "Grid.ColumnDefinitions")!
                .Elements(presentation + "ColumnDefinition")
                .ElementAt(3);

            Assert.Equal("Auto", (string?)bitColumn.Attribute("Width"));
            Assert.Equal("260", (string?)bitColumn.Attribute("MinWidth"));
        }

        var watchHeaderBitColumn = document
            .Descendants(presentation + "Grid")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), "WatchHeaderGrid", StringComparison.Ordinal))
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .ElementAt(5);
        var watchRowBitColumn = GetWatchRowTemplate(document, presentation)
            .Element(presentation + "Grid")!
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .ElementAt(5);

        Assert.Equal("Auto", (string?)watchHeaderBitColumn.Attribute("Width"));
        Assert.Equal("260", (string?)watchHeaderBitColumn.Attribute("MinWidth"));
        Assert.Equal("Auto", (string?)watchRowBitColumn.Attribute("Width"));
        Assert.Equal("260", (string?)watchRowBitColumn.Attribute("MinWidth"));
    }

    private static XElement GetWatchRowTemplate(XDocument document, XNamespace presentation)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document
            .Descendants(presentation + "ListBox")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), "WatchListBox", StringComparison.Ordinal))
            .Element(presentation + "ListBox.ItemTemplate")!
            .Element(presentation + "DataTemplate")!;
    }

    private static XElement GetNamedGrid(XDocument document, XNamespace presentation, XNamespace xaml, string name) =>
        document
            .Descendants(presentation + "Grid")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), name, StringComparison.Ordinal));

    private static XElement GetTemplateGrid(XDocument document, XNamespace presentation, XNamespace xaml, string templateKey) =>
        document
            .Descendants(presentation + "DataTemplate")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), templateKey, StringComparison.Ordinal))
            .Element(presentation + "Grid")!;

    private static string[] GetColumnSharedSizeGroups(XElement grid, XNamespace presentation) =>
        grid
            .Element(presentation + "Grid.ColumnDefinitions")!
            .Elements(presentation + "ColumnDefinition")
            .Select(static element => (string?)element.Attribute("SharedSizeGroup") ?? string.Empty)
            .ToArray();

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
