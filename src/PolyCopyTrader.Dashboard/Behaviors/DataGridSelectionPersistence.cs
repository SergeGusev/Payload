using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PolyCopyTrader.Dashboard.Behaviors;

public static class DataGridSelectionPersistence
{
    public static readonly DependencyProperty KeyPropertiesProperty =
        DependencyProperty.RegisterAttached(
            "KeyProperties",
            typeof(string),
            typeof(DataGridSelectionPersistence),
            new PropertyMetadata(string.Empty, OnKeyPropertiesChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(SelectionPersistenceState),
            typeof(DataGridSelectionPersistence),
            new PropertyMetadata(null));

    public static string GetKeyProperties(DependencyObject element)
    {
        return (string)element.GetValue(KeyPropertiesProperty);
    }

    public static void SetKeyProperties(DependencyObject element, string value)
    {
        element.SetValue(KeyPropertiesProperty, value);
    }

    private static void OnKeyPropertiesChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not DataGrid dataGrid)
        {
            return;
        }

        Detach(dataGrid);

        if (!string.IsNullOrWhiteSpace(args.NewValue as string))
        {
            Attach(dataGrid);
        }
    }

    private static void Attach(DataGrid dataGrid)
    {
        var state = new SelectionPersistenceState(dataGrid);
        dataGrid.SetValue(StateProperty, state);
        dataGrid.SelectionChanged += OnSelectionChanged;
        state.AttachItemsSource();
        ScheduleRestore(dataGrid);
    }

    private static void Detach(DataGrid dataGrid)
    {
        if (dataGrid.GetValue(StateProperty) is not SelectionPersistenceState state)
        {
            return;
        }

        state.DetachItemsSource();
        dataGrid.SelectionChanged -= OnSelectionChanged;
        dataGrid.ClearValue(StateProperty);
    }

    private static void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not DataGrid dataGrid ||
            dataGrid.GetValue(StateProperty) is not SelectionPersistenceState state ||
            state.IsRestoring)
        {
            return;
        }

        if (dataGrid.SelectedItem is { } selectedItem &&
            TryGetKey(dataGrid, selectedItem, out var key))
        {
            state.SelectedKey = key;
        }
    }

    private static void ScheduleRestore(DataGrid dataGrid)
    {
        if (dataGrid.GetValue(StateProperty) is not SelectionPersistenceState state)
        {
            return;
        }

        state.PendingRestore?.Abort();
        state.PendingRestore = dataGrid.Dispatcher.BeginInvoke(
            () => RestoreSelection(dataGrid),
            DispatcherPriority.Background);
    }

    private static void RestoreSelection(DataGrid dataGrid)
    {
        if (dataGrid.GetValue(StateProperty) is not SelectionPersistenceState state ||
            string.IsNullOrWhiteSpace(state.SelectedKey) ||
            dataGrid.ItemsSource is not IEnumerable items)
        {
            return;
        }

        if (dataGrid.SelectedItem is { } selectedItem &&
            TryGetKey(dataGrid, selectedItem, out var currentKey) &&
            string.Equals(currentKey, state.SelectedKey, StringComparison.Ordinal))
        {
            return;
        }

        foreach (var item in items)
        {
            if (item is null ||
                !TryGetKey(dataGrid, item, out var itemKey) ||
                !string.Equals(itemKey, state.SelectedKey, StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                state.IsRestoring = true;
                dataGrid.SelectedItem = item;
            }
            finally
            {
                state.IsRestoring = false;
            }

            return;
        }
    }

    private static bool TryGetKey(DataGrid dataGrid, object item, out string key)
    {
        var properties = GetKeyProperties(dataGrid)
            .Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (properties.Length == 0)
        {
            key = string.Empty;
            return false;
        }

        var itemType = item.GetType();
        var parts = new List<string>(properties.Length + 1)
        {
            itemType.FullName ?? itemType.Name
        };

        foreach (var propertyName in properties)
        {
            var property = itemType.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property is null || property.GetIndexParameters().Length > 0)
            {
                key = string.Empty;
                return false;
            }

            var value = property.GetValue(item);
            parts.Add(property.Name + "=" + Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        key = string.Join("\u001f", parts);
        return true;
    }

    private sealed class SelectionPersistenceState
    {
        private readonly DataGrid dataGrid;
        private readonly DependencyPropertyDescriptor? itemsSourceDescriptor;
        private INotifyCollectionChanged? collection;

        public SelectionPersistenceState(DataGrid dataGrid)
        {
            this.dataGrid = dataGrid;
            itemsSourceDescriptor = DependencyPropertyDescriptor.FromProperty(
                ItemsControl.ItemsSourceProperty,
                typeof(DataGrid));
            itemsSourceDescriptor?.AddValueChanged(dataGrid, OnItemsSourceChanged);
        }

        public string? SelectedKey { get; set; }

        public bool IsRestoring { get; set; }

        public DispatcherOperation? PendingRestore { get; set; }

        public void AttachItemsSource()
        {
            DetachCollection();
            collection = dataGrid.ItemsSource as INotifyCollectionChanged;
            if (collection is not null)
            {
                collection.CollectionChanged += OnCollectionChanged;
            }
        }

        public void DetachItemsSource()
        {
            PendingRestore?.Abort();
            PendingRestore = null;
            DetachCollection();
            itemsSourceDescriptor?.RemoveValueChanged(dataGrid, OnItemsSourceChanged);
        }

        private void OnItemsSourceChanged(object? sender, EventArgs args)
        {
            AttachItemsSource();
            ScheduleRestore(dataGrid);
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            ScheduleRestore(dataGrid);
        }

        private void DetachCollection()
        {
            if (collection is not null)
            {
                collection.CollectionChanged -= OnCollectionChanged;
                collection = null;
            }
        }
    }
}
