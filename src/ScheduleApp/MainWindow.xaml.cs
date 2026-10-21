using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScheduleApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            StateChanged += OnWindowStateChanged;
            UpdateMaximizeIcon();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && !IsInsideButton(e.OriginalSource as DependencyObject))
            {
                ToggleMaximize();
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            UpdateMaximizeIcon();
        }

        private void UpdateMaximizeIcon()
        {
            bool maximized = WindowState == WindowState.Maximized;
            MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
            MaximizeButton.ToolTip = maximized ? "Восстановить" : "Развернуть";
        }

        /// <summary>Проверяет, что двойной щелчок пришёл не по кнопке заголовка.</summary>
        private static bool IsInsideButton(DependencyObject source)
        {
            while (source != null)
            {
                if (source is Button) return true;
                source = source is Visual
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }
            return false;
        }
    }
}
