namespace PlcScope.App.UiTests;

using System.Xml.Linq;

public sealed class MonitorLayoutTests
{
    [Fact]
    public void MainWindow_TitleIsProductName()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var window = document
            .Descendants(presentation + "Window")
            .Single();

        Assert.Equal("PLC Scope", (string?)window.Attribute("Title"));
    }

    [Fact]
    public void Menus_DoNotUseSeparators()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var xamlPaths = new[]
        {
            ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml"),
            ResolveRepoPath("src", "PlcScope.App", "Windows", "ErrorHistoryWindow.xaml"),
            ResolveRepoPath("src", "PlcScope.App", "Windows", "TraceLogWindow.xaml"),
        };

        var menuSeparators = xamlPaths
            .SelectMany(path =>
            {
                var document = XDocument.Load(path);
                return document
                    .Descendants()
                    .Where(element => element.Name == presentation + "Menu" || element.Name == presentation + "ContextMenu")
                    .Descendants(presentation + "Separator")
                    .Select(separator => path);
            });

        Assert.Empty(menuSeparators);
    }

    [Fact]
    public void MonitorAndWatchListItems_UseSharedListBoxItemStyle()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var listBoxItemStyles = document
            .Descendants(presentation + "ListBox.ItemContainerStyle")
            .Elements(presentation + "Style")
            .ToList();

        Assert.All(
            listBoxItemStyles,
            style => Assert.Equal("{StaticResource {x:Type ListBoxItem}}", (string?)style.Attribute("BasedOn")));
    }

    [Fact]
    public void AboutLibrariesList_PreservesGridViewColumnsAndDisablesSelection()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "Windows", "AboutWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var librariesList = document
            .Descendants(presentation + "ListView")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Name"), "LibrariesListView", StringComparison.Ordinal));

        Assert.NotNull(librariesList.Element(presentation + "ListView.View")?.Element(presentation + "GridView"));
        Assert.Contains(
            librariesList
                .Element(presentation + "ListView.ItemContainerStyle")?
                .Descendants(presentation + "Setter") ?? [],
            setter => string.Equals((string?)setter.Attribute("Property"), "IsHitTestVisible", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "False", StringComparison.Ordinal));
    }

    [Fact]
    public void AboutLibrariesList_DoesNotUsePackagePlaceholderLicenses()
    {
        var codePath = ResolveRepoPath("src", "PlcScope.App", "Windows", "AboutWindow.xaml.cs");
        var code = File.ReadAllText(codePath);

        Assert.DoesNotContain("\"See package\"", code, StringComparison.Ordinal);
        Assert.Contains("new LibraryInfo(\"PLC Scope\", appVersion, \"MIT\", \"Application and internal modules\")", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new LibraryInfo(\"PlcScope.Core\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new LibraryInfo(\"PlcScope.Infrastructure\"", code, StringComparison.Ordinal);
        Assert.Contains("new LibraryInfo(\"PlcComm.Slmp\", GetAssemblyVersionText(\"PlcComm.Slmp\"), \"MIT\"", code, StringComparison.Ordinal);
        Assert.Contains("new LibraryInfo(\"PlcComm.KvHostLink\", GetAssemblyVersionText(\"PlcComm.KvHostLink\"), \"MIT\"", code, StringComparison.Ordinal);
        Assert.Contains("new LibraryInfo(\"PlcComm.Toyopuc\", GetAssemblyVersionText(\"PlcComm.Toyopuc\"), \"MIT\"", code, StringComparison.Ordinal);
    }

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
        if (string.Equals(automationId, "WatchTypeComboBox", StringComparison.Ordinal))
            Assert.Equal("{Binding AvailableDataTypes}", (string?)comboBox.Attribute("ItemsSource"));
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
        Assert.Equal("WatchValueTextBox_TextChanged", (string?)textBox.Attribute("TextChanged"));
    }

    [Fact]
    public void ValueTextBoxes_HandleArrowNavigationBeforeTextEditing()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var inlineValueStyle = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), "InlineValueTextBoxStyle", StringComparison.Ordinal));

        Assert.Contains(
            inlineValueStyle.Elements(presentation + "EventSetter"),
            setter => string.Equals((string?)setter.Attribute("Event"), "PreviewKeyDown", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Handler"), "ValueTextBox_PreviewKeyDown", StringComparison.Ordinal));

        var rowTemplate = GetWatchRowTemplate(document, presentation);
        var watchValueBox = rowTemplate
            .Descendants(presentation + "TextBox")
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "WatchValueTextBox", StringComparison.Ordinal)));

        Assert.Equal("ValueTextBox_PreviewKeyDown", (string?)watchValueBox.Attribute("PreviewKeyDown"));
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
    public void CpuMenu_ContainsSlmpOnlyPauseCommand()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);

        var menuItem = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "CpuPauseMenuItem", StringComparison.Ordinal)));

        Assert.Equal("CPU PAUSE", (string?)menuItem.Attribute("Header"));
        Assert.Equal("{Binding CpuPauseCommand}", (string?)menuItem.Attribute("Command"));
        Assert.Equal("{Binding CanIssueCpuPauseControl}", (string?)menuItem.Attribute("IsEnabled"));
        Assert.Equal("{Binding CanShowCpuPauseControl, Converter={StaticResource BoolToVisibilityConverter}}", (string?)menuItem.Attribute("Visibility"));
    }

    [Fact]
    public void ConnectionDialog_ContainsSlmpRemotePasswordBox()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "ConnectionDialog.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var passwordBox = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "ConnectionSlmpRemotePasswordBox", StringComparison.Ordinal)));
        var multidropTextBox = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "ConnectionSlmpMultidropTextBox", StringComparison.Ordinal)));
        var resetRoutingButton = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "ConnectionSlmpResetRoutingDefaultsButton", StringComparison.Ordinal)));
        var slmpRows = document
            .Descendants(presentation + "GroupBox")
            .Single(element => string.Equals((string?)element.Attribute("Header"), "SLMP", StringComparison.Ordinal))
            .Element(presentation + "Grid")!
            .Element(presentation + "Grid.RowDefinitions")!
            .Elements(presentation + "RowDefinition")
            .Count();

        Assert.Equal("TextBox", multidropTextBox.Name.LocalName);
        Assert.Equal("4", (string?)multidropTextBox.Attribute("Grid.Row"));
        Assert.Equal("Button", resetRoutingButton.Name.LocalName);
        Assert.Equal("Reset routing defaults", (string?)resetRoutingButton.Attribute("Content"));
        Assert.Equal("ResetSlmpRoutingDefaultsButton_Click", (string?)resetRoutingButton.Attribute("Click"));
        Assert.Equal("PasswordBox", passwordBox.Name.LocalName);
        Assert.Equal("7", (string?)passwordBox.Attribute("Grid.Row"));
        Assert.True(slmpRows > 7);
    }

    [Fact]
    public void ViewMenu_ContainsAlwaysOnTopSelection()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);

        var menuItem = document
            .Descendants()
            .Single(element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId"
                && string.Equals(attribute.Value, "AlwaysOnTopMenuItem", StringComparison.Ordinal)));

        Assert.Equal("Always on top", menuItem.Attributes().Single(attribute => attribute.Name.LocalName == "AutomationProperties.Name").Value);
        Assert.Equal("True", (string?)menuItem.Attribute("IsCheckable"));
        Assert.Equal("AlwaysOnTopMenuItem_Click", (string?)menuItem.Attribute("Click"));
        Assert.NotNull(menuItem.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "TextBlock"
            && string.Equals((string?)element.Attribute("Text"), "Always on top", StringComparison.Ordinal)));
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

        foreach (var templateKey in new[] { "WordRowTemplate", "PackedBitRowTemplate", "SingleBitRowTemplate", "DWordRowTemplate", "FloatRowTemplate", "ExpandedHeaderTemplate", "ExpandedBitTemplate" })
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
    [InlineData("ExpandedBitTemplate")]
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

        Assert.Equal(5, columnCount);
        Assert.Equal(
            ["MonitorAddressColumn", "MonitorValueColumn", "MonitorHexColumn", "MonitorBitsColumn", "MonitorCommentColumn"],
            GetColumnSharedSizeGroups(grid, presentation));

        if (!string.Equals(templateKey, "ExpandedBitTemplate", StringComparison.Ordinal))
        {
            var commentColumn = grid
                .Elements(presentation + "TextBlock")
                .Single(element => string.Equals((string?)element.Attribute("Text"), "{Binding Comment}", StringComparison.Ordinal))
                .Attribute("Grid.Column")!
                .Value;

            Assert.Equal("4", commentColumn);
        }
    }

    [Fact]
    public void ExpandedBitRow_DoesNotShowSeparateBitIndexColumn()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var template = document
            .Descendants(presentation + "DataTemplate")
            .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), "ExpandedBitTemplate", StringComparison.Ordinal));

        Assert.DoesNotContain(template.Descendants(presentation + "TextBlock"), element =>
            string.Equals((string?)element.Attribute("Text"), "{Binding BitIndex, StringFormat=b{0}}", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandedBitRow_DoesNotOffsetTheWholeRow()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var grid = GetTemplateGrid(document, presentation, xaml, "ExpandedBitTemplate");

        Assert.Null(grid.Attribute("Margin"));
    }

    [Fact]
    public void ExpandedBitRow_IndentsOnlyAddressText()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var grid = GetTemplateGrid(document, presentation, xaml, "ExpandedBitTemplate");
        var addressText = grid
            .Elements(presentation + "TextBlock")
            .Single(element => string.Equals((string?)element.Attribute("Text"), "{Binding Address}", StringComparison.Ordinal));
        var stateButton = grid
            .Elements(presentation + "Button")
            .Single(element => string.Equals((string?)element.Attribute("Content"), "{Binding StateText}", StringComparison.Ordinal));
        var valueText = grid
            .Elements(presentation + "TextBlock")
            .Single(element => string.Equals((string?)element.Attribute("Text"), "{Binding ValueText}", StringComparison.Ordinal));

        Assert.Equal("22,0,0,0", (string?)addressText.Attribute("Margin"));
        Assert.Equal("1", (string?)stateButton.Attribute("Grid.Column"));
        Assert.Equal("3", (string?)valueText.Attribute("Grid.Column"));
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
