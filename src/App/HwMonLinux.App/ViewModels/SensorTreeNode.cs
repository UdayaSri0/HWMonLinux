using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HwMonLinux.App.ViewModels;

public sealed class SensorTreeNode : ViewModelBase
{
    private readonly Dictionary<string, SensorTreeNode> _childrenLookup = new(StringComparer.OrdinalIgnoreCase);

    public SensorTreeNode(string title, SensorRowViewModel? sensor = null)
    {
        Title = title;
        Sensor = sensor;
        Children = new ObservableCollection<SensorTreeNode>();
    }

    public string Title { get; }
    public SensorRowViewModel? Sensor { get; }
    public ObservableCollection<SensorTreeNode> Children { get; }
    public bool IsLeaf => Sensor is not null;

    public string DisplayValue => Sensor?.Value ?? string.Empty;
    public string DisplayMin => Sensor?.Min ?? string.Empty;
    public string DisplayMax => Sensor?.Max ?? string.Empty;

    public SensorTreeNode GetOrAddChild(string title)
    {
        if (!_childrenLookup.TryGetValue(title, out var node))
        {
            node = new SensorTreeNode(title);
            _childrenLookup[title] = node;
            Children.Add(node);
        }

        return node;
    }

    public void SortChildren()
    {
        var sorted = Children.OrderBy(c => c.IsLeaf)
            .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Children.Clear();
        foreach (var child in sorted)
        {
            child.SortChildren();
            Children.Add(child);
        }
    }
}
