namespace PlcScope.App.Tests;

using System.Reflection;
using PlcScope.App.ViewModels;
using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;

public sealed class MonitorRowViewModelFactoryTests
{
    [Fact]
    public void CreateRowViewModel_CreatesEditableRowsForWritableProtocol()
    {
        var viewModel = CreateViewModel();

        AssertRow(CreateEditableRow(viewModel, CreateWordRow()), canEdit: true, canToggleBits: true);
        AssertRow(CreateEditableRow(viewModel, CreatePackedBitRow()), canEdit: false, canToggleBits: true);
        AssertRow(CreateEditableRow(viewModel, CreateSingleBitRow()), canEdit: false, canToggleBits: true);
        AssertRow(CreateEditableRow(viewModel, CreateDWordRow()), canEdit: true, canToggleBits: true);
        AssertRow(CreateEditableRow(viewModel, CreateFloatRow()), canEdit: true, canToggleBits: true);
        AssertRow(CreateEditableRow(viewModel, CreateExpandedWordHeaderRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateEditableRow(viewModel, CreateExpandedBitRow()), canEdit: false, canToggleBits: true);
    }

    [Fact]
    public void CreateReadOnlyRowViewModel_DisablesEditingAndToggles()
    {
        var viewModel = CreateViewModel();

        AssertRow(CreateReadOnlyRow(viewModel, CreateWordRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateReadOnlyRow(viewModel, CreatePackedBitRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateReadOnlyRow(viewModel, CreateSingleBitRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateReadOnlyRow(viewModel, CreateDWordRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateReadOnlyRow(viewModel, CreateFloatRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateReadOnlyRow(viewModel, CreateExpandedWordHeaderRow()), canEdit: false, canToggleBits: false);
        AssertRow(CreateReadOnlyRow(viewModel, CreateExpandedBitRow()), canEdit: false, canToggleBits: false);
    }

    private static MainWindowViewModel CreateViewModel() =>
        new(
            new ThrowingSessionFactory(),
            new NullProjectStore(),
            new InMemorySettingsStore(),
            new NullLogStore());

    private static MonitorRowViewModel CreateEditableRow(MainWindowViewModel viewModel, MonitorRow row) =>
        InvokeFactory(viewModel, "CreateRowViewModel", row);

    private static MonitorRowViewModel CreateReadOnlyRow(MainWindowViewModel viewModel, MonitorRow row) =>
        InvokeFactory(viewModel, "CreateReadOnlyRowViewModel", row);

    private static MonitorRowViewModel InvokeFactory(MainWindowViewModel viewModel, string methodName, MonitorRow row)
    {
        var method = typeof(MainWindowViewModel).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(MonitorRow)],
            modifiers: null);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<MonitorRowViewModel>(method.Invoke(viewModel, [row]));
    }

    private static WordMonitorRow CreateWordRow() =>
        new("D0", 0x1234, [new BitCellState(0, true, "D0.0")], "word comment");

    private static PackedBitMonitorRow CreatePackedBitRow() =>
        new("M0", [new BitCellState(0, true, "M0")], "packed comment");

    private static SingleBitMonitorRow CreateSingleBitRow() =>
        new("M0", true, "single comment");

    private static DWordMonitorRow CreateDWordRow() =>
        new("D0", 0x12345678, [new BitCellState(0, true, "D0.0")], "dword comment");

    private static FloatMonitorRow CreateFloatRow() =>
        new("D0", 1.5f, 0x3FC00000, [new BitCellState(0, true, "D0.0")], "float comment");

    private static ExpandedWordHeaderMonitorRow CreateExpandedWordHeaderRow() =>
        new("D0", 0x1234, [new BitCellState(0, true, "D0.0")], "header comment");

    private static ExpandedBitMonitorRow CreateExpandedBitRow() =>
        new("D0.0", 0, true);

    private static void AssertRow(MonitorRowViewModel row, bool canEdit, bool canToggleBits)
    {
        switch (row)
        {
            case WordRowViewModel word:
                Assert.Equal(canEdit, word.CanEdit);
                Assert.Equal("D0", word.Address);
                Assert.Equal("4660", word.EditableValueText);
                Assert.Equal("0x1234", word.HexText);
                Assert.Equal(canToggleBits, word.Bits.Single().CanToggle);
                break;
            case PackedBitRowViewModel packed:
                Assert.Equal("M0", packed.Address);
                Assert.Equal("M0", packed.SelectionAddress);
                Assert.Equal(canToggleBits, packed.Bits.Single().CanToggle);
                break;
            case SingleBitRowViewModel single:
                Assert.Equal("M0", single.Address);
                Assert.Equal("1", single.ValueText);
                Assert.Equal(canToggleBits, single.CanToggle);
                Assert.Equal(canToggleBits, single.ToggleCommand.CanExecute(null));
                break;
            case DWordRowViewModel dword:
                Assert.Equal(canEdit, dword.CanEdit);
                Assert.Equal("D0", dword.Address);
                Assert.Equal("305419896", dword.EditableValueText);
                Assert.Equal("0x12345678", dword.HexText);
                Assert.Equal(canToggleBits, dword.Bits.Single().CanToggle);
                break;
            case FloatRowViewModel @float:
                Assert.Equal(canEdit, @float.CanEdit);
                Assert.Equal("D0", @float.Address);
                Assert.Equal("1.5", @float.EditableValueText);
                Assert.Equal("0x3FC00000", @float.HexText);
                Assert.Equal(canToggleBits, @float.Bits.Single().CanToggle);
                break;
            case ExpandedWordHeaderRowViewModel header:
                Assert.Equal("D0", header.Address);
                Assert.Equal("4660", header.ValueText);
                Assert.Equal("0x1234", header.HexText);
                Assert.False(header.Bits.Single().CanToggle);
                break;
            case ExpandedBitRowViewModel expandedBit:
                Assert.Equal("D0.0", expandedBit.Address);
                Assert.Equal("D0", expandedBit.WordAddress);
                Assert.Equal("1", expandedBit.ValueText);
                Assert.Equal(canToggleBits, expandedBit.CanToggle);
                Assert.Equal(canToggleBits, expandedBit.ToggleCommand.CanExecute(null));
                break;
            default:
                throw new Xunit.Sdk.XunitException($"Unexpected row type {row.GetType().Name}.");
        }
    }

    private sealed class ThrowingSessionFactory : IPlcSessionFactory
    {
        public Task<IPlcSession> CreateAsync(ConnectionSettings settings, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test does not connect to a PLC.");
    }

    private sealed class NullProjectStore : IProjectStore
    {
        public Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProjectFile());

        public Task SaveAsync(string path, ProjectFile project, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
