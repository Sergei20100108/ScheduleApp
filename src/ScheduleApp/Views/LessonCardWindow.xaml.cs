using System.Windows;
using ScheduleApp.ViewModels;

namespace ScheduleApp.Views
{
    public partial class LessonCardWindow : Window
    {
        public LessonCardWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is LessonCardViewModel vm)
                {
                    vm.RequestClose += ok =>
                    {
                        DialogResult = ok;
                        Close();
                    };
                }
            };
        }
    }
}
