namespace PlcScope.App.Tests;

using System.Xml.Linq;

public sealed class AppThemeResourceTests
{
    [Fact]
    public void DarkThemeDefinitions_MatchInitialAppXamlBrushes()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var appBrushes = document
            .Descendants(presentation + "SolidColorBrush")
            .Select(element => new
            {
                Key = (string?)element.Attribute(xaml + "Key"),
                Color = (string?)element.Attribute("Color"),
            })
            .Where(brush => brush.Key?.StartsWith("App", StringComparison.Ordinal) is true)
            .ToDictionary(
                brush => brush.Key!,
                brush => brush.Color!,
                StringComparer.Ordinal);

        var darkTheme = App.ThemeColorDefinitions["Dark"];

        Assert.Equal(darkTheme.OrderBy(static pair => pair.Key), appBrushes.OrderBy(static pair => pair.Key));
    }

    [Fact]
    public void SelectionStyles_SetReadableForeground()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.NotNull(document
            .Descendants(presentation + "SolidColorBrush")
            .SingleOrDefault(element => string.Equals((string?)element.Attribute(xaml + "Key"), "AppSelectionForegroundBrush", StringComparison.Ordinal)));

        foreach (var controlType in new[] { "ComboBoxItem", "MenuItem", "ListBoxItem", "DataGridCell" })
        {
            var style = document
                .Descendants(presentation + "Style")
                .Single(element => string.Equals((string?)element.Attribute("TargetType"), $"{{x:Type {controlType}}}", StringComparison.Ordinal));

            Assert.Contains(
                style.Descendants(presentation + "Setter"),
                setter => string.Equals((string?)setter.Attribute("Property"), "Foreground", StringComparison.Ordinal)
                    && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppSelectionForegroundBrush}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MenuItems_UseAppTemplateInsteadOfSystemSelectionRendering()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var menuItemStyle = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type MenuItem}", StringComparison.Ordinal));
        var template = menuItemStyle
            .Descendants(presentation + "ControlTemplate")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type MenuItem}", StringComparison.Ordinal));

        Assert.Contains(
            template.Descendants(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "Foreground", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppSelectionForegroundBrush}", StringComparison.Ordinal));
        Assert.Contains(
            template.Descendants(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("TargetName"), "MenuItemBorder", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Property"), "Background", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppSelectionBrush}", StringComparison.Ordinal));
        Assert.Contains(
            menuItemStyle.Elements(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "FocusVisualStyle", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{x:Null}", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextMenus_UseAppTemplateInsteadOfSystemBorderRendering()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var contextMenuStyle = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type ContextMenu}", StringComparison.Ordinal));
        var template = contextMenuStyle
            .Descendants(presentation + "ControlTemplate")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type ContextMenu}", StringComparison.Ordinal));

        Assert.Contains(
            contextMenuStyle.Elements(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "BorderBrush", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppBorderSubtleBrush}", StringComparison.Ordinal));
        Assert.NotNull(template.Descendants(presentation + "ItemsPresenter").SingleOrDefault());
    }

    [Fact]
    public void ListBoxItems_DoNotShowSystemFocusRectangle()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var style = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type ListBoxItem}", StringComparison.Ordinal));

        Assert.Contains(
            style.Elements(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "FocusVisualStyle", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{x:Null}", StringComparison.Ordinal));
    }

    [Fact]
    public void ButtonHoverBackgrounds_UseStylePropertiesSoSpecialButtonsKeepContrast()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        var defaultButtonStyle = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type Button}", StringComparison.Ordinal)
                && element.Attribute(xaml + "Key") is null);

        Assert.Contains(
            defaultButtonStyle.Elements(presentation + "Style.Triggers").Descendants(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "Background", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppHoverBrush}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            defaultButtonStyle.Descendants(presentation + "ControlTemplate").Descendants(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("TargetName"), "ButtonBorder", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Property"), "Background", StringComparison.Ordinal)
                && ((string?)setter.Attribute("Value") is "{DynamicResource AppHoverBrush}" or "{DynamicResource AppPressedBrush}"));

        foreach (var (styleKey, hoverBrush) in new[]
        {
            ("AccentButtonStyle", "AppAccentHoverBrush"),
            ("SuccessButtonStyle", "AppSuccessSoftBrush"),
            ("WarningButtonStyle", "AppWarningSoftBrush"),
            ("DangerButtonStyle", "AppDangerSoftBrush"),
        })
        {
            var style = document
                .Descendants(presentation + "Style")
                .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), styleKey, StringComparison.Ordinal));

            Assert.Contains(
                style.Elements(presentation + "Style.Triggers").Descendants(presentation + "Setter"),
                setter => string.Equals((string?)setter.Attribute("Property"), "Background", StringComparison.Ordinal)
                    && string.Equals((string?)setter.Attribute("Value"), $"{{DynamicResource {hoverBrush}}}", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void MonitorTextStyles_UseSelectionForegroundWhenListBoxItemIsSelected()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "MainWindow.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        foreach (var styleKey in new[] { "AddressTextStyle", "ValueTextStyle", "HexTextStyle", "CommentTextStyle" })
        {
            var style = document
                .Descendants(presentation + "Style")
                .Single(element => string.Equals((string?)element.Attribute(xaml + "Key"), styleKey, StringComparison.Ordinal));
            var selectionTrigger = style
                .Descendants(presentation + "DataTrigger")
                .Single(trigger => ((string?)trigger.Attribute("Binding"))?.Contains("AncestorType={x:Type ListBoxItem}", StringComparison.Ordinal) is true);

            Assert.Contains(
                selectionTrigger.Descendants(presentation + "Setter"),
                setter => string.Equals((string?)setter.Attribute("Property"), "Foreground", StringComparison.Ordinal)
                    && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppSelectionForegroundBrush}", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("DataGridColumnHeader")]
    [InlineData("GridViewColumnHeader")]
    public void GridHeaders_UseAppTemplateInsteadOfSystemHeaderRendering(string controlType)
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var style = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), $"{{x:Type {controlType}}}", StringComparison.Ordinal));
        var template = style
            .Descendants(presentation + "ControlTemplate")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), $"{{x:Type {controlType}}}", StringComparison.Ordinal));

        Assert.Contains(
            template.Descendants(presentation + "ContentPresenter"),
            presenter => string.Equals((string?)presenter.Attribute("TextElement.Foreground"), "{TemplateBinding Foreground}", StringComparison.Ordinal));
        Assert.Contains(
            template.Descendants(presentation + "Border"),
            border => string.Equals((string?)border.Attribute("Background"), "{TemplateBinding Background}", StringComparison.Ordinal));
    }

    [Fact]
    public void Separators_UseAppBorderBrush()
    {
        var xamlPath = ResolveRepoPath("src", "PlcScope.App", "App.xaml");
        var document = XDocument.Load(xamlPath);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var style = document
            .Descendants(presentation + "Style")
            .Single(element => string.Equals((string?)element.Attribute("TargetType"), "{x:Type Separator}", StringComparison.Ordinal));

        Assert.Contains(
            style.Elements(presentation + "Setter"),
            setter => string.Equals((string?)setter.Attribute("Property"), "Background", StringComparison.Ordinal)
                && string.Equals((string?)setter.Attribute("Value"), "{DynamicResource AppBorderSubtleBrush}", StringComparison.Ordinal));
        Assert.NotNull(style.Descendants(presentation + "ControlTemplate").SingleOrDefault());
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
