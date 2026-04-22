namespace PlcScope.App.Selectors;

using System.Windows;
using System.Windows.Controls;
using PlcScope.App.ViewModels;

public sealed class MonitorRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? WordTemplate { get; set; }
    public DataTemplate? PackedBitsTemplate { get; set; }
    public DataTemplate? SingleBitTemplate { get; set; }
    public DataTemplate? DWordTemplate { get; set; }
    public DataTemplate? FloatTemplate { get; set; }
    public DataTemplate? ExpandedHeaderTemplate { get; set; }
    public DataTemplate? ExpandedBitTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item switch
        {
            WordRowViewModel => WordTemplate,
            PackedBitRowViewModel => PackedBitsTemplate,
            SingleBitRowViewModel => SingleBitTemplate,
            DWordRowViewModel => DWordTemplate,
            FloatRowViewModel => FloatTemplate,
            ExpandedWordHeaderRowViewModel => ExpandedHeaderTemplate,
            ExpandedBitRowViewModel => ExpandedBitTemplate,
            _ => base.SelectTemplate(item, container),
        };
}
