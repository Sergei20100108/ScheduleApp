using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScheduleApp.ViewModels;
using ScheduleApp.ViewModels.ScheduleGrid;

namespace ScheduleApp.Views
{
    /// <summary>
    /// Код-бихайнд отвечает только за UI-механику: синхронизацию прокрутки заголовков,
    /// drag&amp;drop и контекстные меню. Вся логика находится во ViewModel.
    /// </summary>
    public partial class ScheduleView : UserControl
    {
        private const string TileFormat = "ScheduleApp.LessonTile";
        private Point _dragStart;
        private LessonTile _dragTile;

        public ScheduleView()
        {
            InitializeComponent();
            BodyScroll.ScrollChanged += (s, e) =>
            {
                if (e.HorizontalChange != 0 || Math.Abs(HeaderScroll.HorizontalOffset - BodyScroll.HorizontalOffset) > 0.5)
                    HeaderScroll.ScrollToHorizontalOffset(BodyScroll.HorizontalOffset);
            };
        }

        private ScheduleTabViewModel Vm => DataContext as ScheduleTabViewModel;

        // ================= Drag & Drop =================

        private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is LessonTile tile)
            {
                if (e.ClickCount == 2)
                {
                    Vm?.OpenLessonCommand.Execute(tile);
                    e.Handled = true;
                    return;
                }
                _dragStart = e.GetPosition(this);
                _dragTile = tile;
            }
        }

        private void Tile_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragTile == null) return;
            var pos = e.GetPosition(this);
            if (System.Math.Abs(pos.X - _dragStart.X) < 6 && System.Math.Abs(pos.Y - _dragStart.Y) < 6) return;

            if (_dragTile.IsLocked)
            {
                Vm.StatusText = "Занятие заблокировано — сначала снимите блокировку (правый клик → Разблокировать)";
                _dragTile = null;
                return;
            }

            var data = new DataObject(TileFormat, _dragTile);
            var tile = _dragTile;
            _dragTile = null;
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }

        private void Cell_DragOver(object sender, DragEventArgs e)
        {
            var vm = Vm;
            if (vm == null || !e.Data.GetDataPresent(TileFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
            var tile = (LessonTile)e.Data.GetData(TileFormat);
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            var error = vm.ValidateMove(tile, cell);
            e.Effects = error == null ? DragDropEffects.Move : DragDropEffects.None;
            vm.StatusText = error ?? $"Перенос «{tile.Line1}» → {cell?.Hint?.Split('\n')[0]}: отпустите для применения";
            e.Handled = true;
        }

        private void Cell_Drop(object sender, DragEventArgs e)
        {
            var vm = Vm;
            if (vm == null || !e.Data.GetDataPresent(TileFormat)) return;
            var tile = (LessonTile)e.Data.GetData(TileFormat);
            var cell = (sender as FrameworkElement)?.DataContext as GridCell;
            vm.ExecuteMove(tile, cell);
            e.Handled = true;
        }

        // ================= Контекстные меню =================

        private void Cell_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not GridCell cell) return;
            var vm = Vm;
            if (vm == null) return;

            var menu = new ContextMenu();
            var add = new MenuItem { Header = "Добавить занятие…" };
            add.Click += (s, a) => vm.AddLessonCommand.Execute(new CellTarget { Cell = cell });
            menu.Items.Add(add);

            var clear = new MenuItem { Header = "Очистить ячейку" };
            clear.IsEnabled = cell.Tiles.Count > 0;
            clear.Click += (s, a) =>
            {
                if (!vm.Dialogs.Confirm($"Удалить все занятия из ячейки ({cell.Tiles.Count})?", "Очистка ячейки")) return;
                foreach (var t in cell.Tiles.ToList()) vm.DeleteLessonCommand.Execute(t);
            };
            menu.Items.Add(clear);

            menu.PlacementTarget = fe;
            menu.IsOpen = true;
            e.Handled = true;
        }

        private void Tile_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not LessonTile tile) return;
            var vm = Vm;
            if (vm == null) return;

            var menu = new ContextMenu();

            var open = new MenuItem { Header = "Открыть карточку занятия…" };
            open.Click += (s, a) => vm.OpenLessonCommand.Execute(tile);
            menu.Items.Add(open);

            var lockItem = new MenuItem { Header = tile.IsLocked ? "Разблокировать" : "Заблокировать (защитить от перегенерации)" };
            lockItem.Click += (s, a) => vm.ToggleLockCommand.Execute(tile);
            menu.Items.Add(lockItem);

            menu.Items.Add(new Separator());

            var delete = new MenuItem { Header = "Удалить занятие" };
            delete.Click += (s, a) => vm.DeleteLessonCommand.Execute(tile);
            menu.Items.Add(delete);

            menu.PlacementTarget = fe;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
