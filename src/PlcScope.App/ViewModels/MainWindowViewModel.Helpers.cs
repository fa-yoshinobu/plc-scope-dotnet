namespace PlcScope.App.ViewModels;

using PlcScope.Core.Models;
using PlcScope.Core.Services;

public partial class MainWindowViewModel
{
    private string FormatWordValue(ushort value) =>
        MonitorDataType == ValueDataType.Int16
            ? RawValueConverter.FormatInt16(unchecked((short)value), DisplayRadix)
            : NumericFormatter.FormatWord(value, DisplayRadix);

    private string FormatDWordValue(uint value) =>
        MonitorDataType == ValueDataType.Int32
            ? RawValueConverter.FormatInt32(unchecked((int)value), DisplayRadix)
            : NumericFormatter.FormatDWord(value, DisplayRadix);

    private DeviceFamilyDefinition ResolveDeviceFamilyForAddress(string address)
    {
        var trimmed = address.Trim();
        foreach (var family in GetSortedDeviceFamilies(SelectedProtocol))
        {
            if (trimmed.StartsWith(family.Code, StringComparison.OrdinalIgnoreCase))
                return family;
        }

        return SelectedDeviceFamily;
    }

    private IReadOnlyList<DeviceFamilyDefinition> GetSortedDeviceFamilies(ProtocolDefinition protocol)
    {
        var keyenceMode = ConnectionSettings.KeyenceDeviceMode;
        if (_sortedFamiliesByCodeLength is null
            || _sortedFamilyProtocol != protocol.Kind
            || _sortedFamilyKeyenceMode != keyenceMode)
        {
            _sortedFamiliesByCodeLength = ProtocolCatalog.GetDeviceFamilies(protocol, keyenceMode)
                .OrderByDescending(static family => family.Code.Length)
                .ToArray();
            _sortedFamilyProtocol = protocol.Kind;
            _sortedFamilyKeyenceMode = keyenceMode;
        }

        return _sortedFamiliesByCodeLength;
    }

    private void InvalidateSortedDeviceFamilyCache()
    {
        _sortedFamiliesByCodeLength = null;
        _sortedFamilyProtocol = null;
        _sortedFamilyKeyenceMode = null;
    }

    private ValueDataType NormalizeDWordOnlyDataType(DeviceFamilyDefinition family, ValueDataType dataType)
        => WatchDataTypePolicy.NormalizeDWordOnlyDataType(SelectedProtocol.Kind, family, dataType);
}
