using System.Windows;
using System.Windows.Controls;
using Curia.Models;

namespace Curia.Helpers;

public class SyncLogTemplateSelector : DataTemplateSelector
{
    public DataTemplate? SeparatorTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }
    public DataTemplate? StepTemplate { get; set; }
    public DataTemplate? SectionTemplate { get; set; }
    public DataTemplate? EmptyTemplate { get; set; }
    public DataTemplate? DefaultTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is SyncLogEntry entry)
        {
            return entry.Kind switch
            {
                SyncLogEntryKind.Separator => SeparatorTemplate,
                SyncLogEntryKind.Header    => HeaderTemplate,
                SyncLogEntryKind.Step      => StepTemplate,
                SyncLogEntryKind.Section   => SectionTemplate,
                SyncLogEntryKind.Empty     => EmptyTemplate,
                _                          => DefaultTemplate
            } ?? DefaultTemplate;
        }
        return DefaultTemplate;
    }
}
