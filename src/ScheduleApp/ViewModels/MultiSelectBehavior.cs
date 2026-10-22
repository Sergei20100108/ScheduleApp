using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace ScheduleApp.ViewModels
{
    public static class MultiSelectBehavior
    {
        public static readonly DependencyProperty BindSelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "BindSelectedItems",
                typeof(IList),
                typeof(MultiSelectBehavior),
                new PropertyMetadata(null, OnBindSelectedItemsChanged));

        public static void SetBindSelectedItems(DependencyObject element, IList value) =>
            element.SetValue(BindSelectedItemsProperty, value);

        public static IList GetBindSelectedItems(DependencyObject element) =>
            (IList)element.GetValue(BindSelectedItemsProperty);

        private static void OnBindSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is DataGrid grid)) return;
            if (e.OldValue is IList)
                grid.SelectionChanged -= GridOnSelectionChanged;
            if (e.NewValue is IList)
            {
                grid.SelectionChanged += GridOnSelectionChanged;
                Sync(grid, (IList)e.NewValue);
            }
        }

        private static void GridOnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(sender is DataGrid grid)) return;
            var list = GetBindSelectedItems(grid);
            if (list == null) return;
            list.Clear();
            foreach (var item in grid.SelectedItems)
                list.Add(item);
        }

        private static void Sync(DataGrid grid, IList list)
        {
            list.Clear();
            foreach (var item in grid.SelectedItems)
                list.Add(item);
        }
    }
}
