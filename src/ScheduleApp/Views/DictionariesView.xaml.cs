using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScheduleApp.ViewModels;

namespace ScheduleApp.Views
{
    public partial class DictionariesView : UserControl
    {
        public DictionariesView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is DictionariesViewModel old) old.RowAdded -= OnRowAdded;
            if (e.NewValue is DictionariesViewModel vm) vm.RowAdded += OnRowAdded;
        }

        /// <summary>После добавления строки переводим фокус в её ячейку — можно сразу ввести название.</summary>
        private void OnRowAdded(object sender, EventArgs e)
        {
            if (!(DataContext is DictionariesViewModel vm)) return;
            var item = vm.CurrentArea?.SelectedItem;
            if (item == null) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var grid in FindDataGrids(this))
                {
                    if (!grid.IsVisible) continue;
                    grid.ScrollIntoView(item);
                    grid.SelectedItem = item;
                    grid.CurrentItem = item;
                    if (grid.Columns.Count > 0) grid.CurrentColumn = grid.Columns[0];
                    grid.Focus();
                    grid.BeginEdit();
                    break;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private static IEnumerable<DataGrid> FindDataGrids(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is DataGrid grid) yield return grid;
                foreach (var nested in FindDataGrids(child)) yield return nested;
            }
        }
    }
}
