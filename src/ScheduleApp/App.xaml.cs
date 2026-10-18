using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Services.Data;
using ScheduleApp.Services.Dialogs;
using ScheduleApp.Services.Export;
using ScheduleApp.Services.Scheduling;
using ScheduleApp.Services.Settings;
using ScheduleApp.Services.Validation;
using ScheduleApp.ViewModels;

namespace ScheduleApp
{
    public partial class App : Application
    {
        private IServiceProvider _services;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            try
            {
                Bootstrap();
            }
            catch (Exception ex)
            {
                Log("FATAL STARTUP: " + ex);
                MessageBox.Show("Ошибка запуска приложения: " + ex.Message, "Расписание",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
            }
        }

        private void Bootstrap()
        {
            var services = new ServiceCollection();

            services.AddDbContext<ScheduleDbContext>(options =>
                options.UseSqlite(DbConnectionInfo.ConnectionString), ServiceLifetime.Transient);

            services.AddSingleton<ISettingsService, JsonSettingsService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<SchedulingAlgorithmRegistry>();
            services.AddSingleton<IScheduleDataService, ScheduleDataService>();
            services.AddSingleton<IValidationService, ValidationService>();
            services.AddSingleton<IGenerationService, GenerationService>();
            services.AddSingleton<IExcelExportService, ExcelExportService>();
            services.AddSingleton<IPdfExportService, PdfExportService>();
            services.AddSingleton<MainViewModel>();

            _services = services.BuildServiceProvider();

            // Инициализация базы данных (Code-First + тестовые данные при первом запуске)
            using (var scope = _services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                DbInitializer.Initialize(db);
            }

            var settings = _services.GetRequiredService<ISettingsService>();
            ThemeManager.Apply(settings.Current.Theme);

            var mainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            MainWindow = mainWindow;

            var vm = (MainViewModel)mainWindow.DataContext;
            mainWindow.Loaded += async (s, a) =>
            {
                try
                {
                    await vm.InitializeAsync();
                }
                catch (Exception ex)
                {
                    Log("ERROR InitializeAsync: " + ex);
                    MessageBox.Show("Ошибка инициализации: " + ex.Message, "Расписание",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            mainWindow.Show();
        }

        internal static void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Логирование не должно ломать приложение
            }
        }

        internal static string LogPath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scheduleapp.log");

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("UNHANDLED: " + e.Exception);
            MessageBox.Show(
                "Непредвиденная ошибка: " + e.Exception.Message + "\n\n" + e.Exception.StackTrace,
                "Расписание", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
