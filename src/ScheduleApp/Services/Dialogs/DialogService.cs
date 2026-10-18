using System.Windows;

namespace ScheduleApp.Services.Dialogs
{
    public interface IDialogService
    {
        void Info(string message, string title = "Расписание");
        void Warn(string message, string title = "Предупреждение");
        void Error(string message, string title = "Ошибка");
        bool Confirm(string message, string title = "Подтверждение");
        string SaveFilePath(string filter, string defaultName);
        string OpenFilePath(string filter);
    }

    public sealed class DialogService : IDialogService
    {
        public void Info(string message, string title = "Расписание") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public void Warn(string message, string title = "Предупреждение") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void Error(string message, string title = "Ошибка") =>
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

        public bool Confirm(string message, string title = "Подтверждение") =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        public string SaveFilePath(string filter, string defaultName)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = filter, FileName = defaultName };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        public string OpenFilePath(string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = filter };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }
    }
}
