namespace PlcScope.App.UiTests;

using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

[Collection(UiTestCollection.Name)]
public sealed class MainWindowUiTests
{
    [Fact]
    public void MainWindow_ShowsProjectFileNamePlcModelAndSingleConnectionButton()
    {
        var app = LaunchApp();
        try
        {
            using var automation = new UIA3Automation();
            var window = WaitForMainWindow(app, automation);
            var condition = automation.ConditionFactory;

            var projectText = FindFirst(window, condition.ByAutomationId("ProjectNameTextBlock"));
            var plcText = FindFirst(window, condition.ByAutomationId("SelectedPlcModelStatusText"));
            var connectionButton = FindFirst(window, condition.ByAutomationId("ConnectionToggleButton")).AsButton();

            Assert.Equal("Untitled", projectText.Name);
            Assert.Equal("PLC: iQ-R", plcText.Name);
            Assert.Equal("Connect", connectionButton.Name);
            Assert.Null(window.FindFirstDescendant(condition.ByAutomationId("DisconnectButton")));
        }
        finally
        {
            CloseApp(app);
        }
    }

    [Fact]
    public void MonitorAndWatchSurfaces_AreAutomationAccessible()
    {
        var app = LaunchApp();
        try
        {
            using var automation = new UIA3Automation();
            var window = WaitForMainWindow(app, automation);
            var condition = automation.ConditionFactory;

            Assert.NotNull(FindFirst(window, condition.ByAutomationId("MonitorListBox")));

            FindFirst(window, condition.ByAutomationId("WatchTab")).Click();
            Assert.NotNull(FindFirst(window, condition.ByAutomationId("WatchListBox")));
        }
        finally
        {
            CloseApp(app);
        }
    }

    [Fact]
    public void MonitorStartAddress_EditCommitsWithEnter()
    {
        var app = LaunchApp();
        try
        {
            using var automation = new UIA3Automation();
            var window = WaitForMainWindow(app, automation);
            var condition = automation.ConditionFactory;

            var startAddress = FindFirst(window, condition.ByAutomationId("MonitorStartAddressTextBox")).AsTextBox();
            SetTextBoxValue(startAddress, "D10");

            Retry.WhileFalse(
                () => string.Equals(startAddress.Text, "D10", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(2));
            Assert.Equal("D10", startAddress.Text);
        }
        finally
        {
            CloseApp(app);
        }
    }

    [Fact]
    public void MonitorAndWatchScrolling_UpdateUiAutomationState()
    {
        var projectPath = CreateLargeProjectFile();
        var app = LaunchApp(projectPath);
        try
        {
            using var automation = new UIA3Automation();
            var window = WaitForMainWindow(app, automation);
            var condition = automation.ConditionFactory;
            var fileName = Path.GetFileName(projectPath);

            Retry.WhileFalse(
                () => string.Equals(FindFirst(window, condition.ByAutomationId("ProjectNameTextBlock")).Name, fileName, StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: true);

            var initialMonitorStart = GetStateInt(window, "monitorStart");
            var startAddress = FindFirst(window, condition.ByAutomationId("MonitorStartAddressTextBox")).AsTextBox();
            SetTextBoxValue(startAddress, "D80");
            Retry.WhileFalse(
                () => GetStateInt(window, "monitorStart") > initialMonitorStart,
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: true,
                timeoutMessage: $"Expected monitor start to move after start address edit. Current state: {window.HelpText}");

            FindFirst(window, condition.ByAutomationId("WatchTab")).Click();
            var watchGrid = FindFirst(window, condition.ByAutomationId("WatchListBox"));
            var initialWatchStart = GetStateInt(window, "watchStart");
            ScrollDownUntilStateIncreases(window, watchGrid, condition, "watchStart", initialWatchStart);
        }
        finally
        {
            CloseApp(app);
            File.Delete(projectPath);
        }
    }

    [Fact]
    public void MonitorInlineValueFocus_TogglesInlineEditingState()
    {
        var app = LaunchApp();
        try
        {
            using var automation = new UIA3Automation();
            var window = WaitForMainWindow(app, automation);
            var condition = automation.ConditionFactory;

            Retry.WhileFalse(
                () => GetStateInt(window, "monitorRows") > 0,
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: true,
                timeoutMessage: $"Monitor rows were not generated. Current state: {window.HelpText}");

            var valueBox = FindFirst(window, condition.ByAutomationId("MonitorInlineValueTextBox")).AsTextBox();
            valueBox.Focus();
            Retry.WhileFalse(
                () => GetStateBool(window, "inlineEditing"),
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: true);

            FindFirst(window, condition.ByAutomationId("MonitorStartAddressTextBox")).Focus();
            Retry.WhileFalse(
                () => !GetStateBool(window, "inlineEditing"),
                timeout: TimeSpan.FromSeconds(5),
                throwOnTimeout: true);
        }
        finally
        {
            CloseApp(app);
        }
    }

    private static Application LaunchApp(params string[] args)
    {
        var appPath = ResolveAppPath();
        ResetAppData(appPath);
        var startInfo = new ProcessStartInfo(appPath)
        {
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return Application.Launch(startInfo);
    }

    private static void CloseApp(Application app)
    {
        if (!app.HasExited)
            app.Close(killIfCloseFails: true);

        app.Dispose();
    }

    private static Window WaitForMainWindow(Application app, UIA3Automation automation)
    {
        var retry = Retry.WhileNull(
            () => app.GetMainWindow(automation),
            timeout: TimeSpan.FromSeconds(10),
            throwOnTimeout: true);
        return retry.Result!;
    }

    private static AutomationElement FindFirst(AutomationElement element, ConditionBase condition)
    {
        var retry = Retry.WhileNull(
            () => element.FindFirstDescendant(condition),
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: "Could not find element.");
        return retry.Result!;
    }

    private static void SetTextBoxValue(TextBox textBox, string value)
    {
        textBox.Focus();
        textBox.Patterns.Value.Pattern.SetValue(value);
        Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ENTER);
    }

    private static void ScrollDownUntilStateIncreases(
        Window window,
        AutomationElement element,
        ConditionFactory condition,
        string stateKey,
        int initialValue)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            ScrollElementDown(element, condition);
            Thread.Sleep(100);
            if (GetStateInt(window, stateKey) > initialValue)
                return;
        }

        Assert.True(
            GetStateInt(window, stateKey) > initialValue,
            $"Expected '{stateKey}' to increase from {initialValue}. Current state: {window.HelpText}. Descendants: {DescribeDescendants(element)}");
    }

    private static void ScrollElementDown(AutomationElement element, ConditionFactory condition)
    {
        element.Focus();

        var scrollBar = element.FindFirstDescendant(condition.VerticalScrollBar());
        if (scrollBar is not null)
        {
            scrollBar.AsVerticalScrollBar().ScrollDownLarge();
            return;
        }

        var lastItem = element.FindAllDescendants(condition.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem)).LastOrDefault()
            ?? element.FindAllDescendants(condition.ByControlType(FlaUI.Core.Definitions.ControlType.DataItem)).LastOrDefault();
        if (lastItem is not null)
        {
            TryFocus(lastItem);
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.NEXT);
            Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.DOWN);
            return;
        }

        var bounds = element.BoundingRectangle;
        Mouse.MoveTo(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        Mouse.Scroll(-5);
    }

    private static void TryFocus(AutomationElement element)
    {
        try
        {
            element.Focus();
        }
        catch
        {
        }
    }

    private static string DescribeDescendants(AutomationElement element)
    {
        return string.Join(
            " | ",
            element.FindAllDescendants()
                .Take(120)
                .Select(static descendant => $"{Safe(() => descendant.ControlType.ToString())}:{Safe(() => descendant.AutomationId)}:{Safe(() => descendant.Name)}"));
    }

    private static string Safe(Func<string> valueFactory)
    {
        try
        {
            return valueFactory();
        }
        catch
        {
            return "<unsupported>";
        }
    }

    private static int GetStateInt(Window window, string key) =>
        int.Parse(GetStateValue(window, key), System.Globalization.CultureInfo.InvariantCulture);

    private static bool GetStateBool(Window window, string key) =>
        bool.Parse(GetStateValue(window, key));

    private static string GetStateValue(Window window, string key)
    {
        var prefix = $"{key}=";
        var stateItems = window.HelpText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var item = stateItems.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            throw new InvalidOperationException($"UI automation state does not contain '{key}': {window.HelpText}");

        return item[prefix.Length..];
    }

    private static string CreateLargeProjectFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plc-scope-ui-{Guid.NewGuid():N}.json");
        var watchItems = string.Join(
            "," + Environment.NewLine,
            Enumerable.Range(0, 160).Select(index =>
                $$"""{"address":"D{{index}}","dataType":"UInt16","displayRadix":"Decimal","comment":"watch {{index}}"}"""));
        var json = $$"""
        {
          "projectVersion": "1.0",
          "blocks": [
            {
              "title": "Main block",
              "protocol": "Slmp",
              "deviceFamilyCode": "D",
              "deviceKind": "Word",
              "startAddress": "D0",
              "itemCount": 160,
              "displayMode": "Word",
              "displayRadix": "Decimal"
            }
          ],
          "watchItems": [
            {{watchItems}}
          ]
        }
        """;
        File.WriteAllText(path, json);
        return path;
    }

    private static void ResetAppData(string appPath)
    {
        var appDirectory = Path.GetDirectoryName(appPath)!;
        foreach (var fileName in new[] { "settings.json", "trace.log.jsonl", "error.log.jsonl" })
        {
            var path = Path.Combine(appDirectory, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static string ResolveAppPath()
    {
        var root = FindRepositoryRoot(AppContext.BaseDirectory);
        var configuration = IsReleaseBuild() ? "Release" : "Debug";
        var appPath = Path.Combine(root, "src", "PlcScope.App", "bin", configuration, "net9.0-windows", "PlcScope.App.exe");
        if (!File.Exists(appPath))
            throw new FileNotFoundException("The WPF application executable was not built.", appPath);

        return appPath;
    }

    private static bool IsReleaseBuild()
    {
#if DEBUG
        return false;
#else
        return true;
#endif
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PlcScopeDotNet.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find PlcScopeDotNet.sln from the test output directory.");
    }
}
