using System;
using System.Threading.Tasks;
using ScheduleApp.Services.Data;
using ScheduleApp.Services.Dialogs;
using ScheduleApp.Services.Export;
using ScheduleApp.Services.Scheduling;
using ScheduleApp.Services.Settings;
using ScheduleApp.Services.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleApp.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private string _statusText = "Готово";
        private int _selectedTabIndex;

        public MainViewModel(IServiceScopeFactory scopeFactory, IGenerationService generation,
            IScheduleDataService data, IDialogService dialogs, SchedulingAlgorithmRegistry registry,
            ISettingsService settings, IValidationService validation, IExcelExportService excel, IPdfExportService pdf)
        {
            Dialogs = dialogs;
            Schedule = new ScheduleTabViewModel(data, validation, dialogs, excel, pdf, settings,
                () => OnDataChanged(nameof(Schedule)));
            Dictionaries = new DictionariesViewModel(scopeFactory, dialogs, () => OnDataChanged(nameof(Dictionaries)));
            Constraints = new ConstraintsViewModel(scopeFactory, dialogs, () => OnDataChanged(nameof(Constraints)));
            Generation = new GenerationViewModel(generation, data, dialogs, registry, () => OnDataChanged(nameof(Generation)));
            Reports = new ReportsViewModel(scopeFactory, validation);
            Settings = new SettingsViewModel(settings, () => OnDataChanged(nameof(Settings)));

            Generation.Complexity = settings.Current.DefaultComplexity;
        }

        public IDialogService Dialogs { get; }
        public ScheduleTabViewModel Schedule { get; }
        public DictionariesViewModel Dictionaries { get; }
        public ConstraintsViewModel Constraints { get; }
        public GenerationViewModel Generation { get; }
        public ReportsViewModel Reports { get; }
        public SettingsViewModel Settings { get; }

        public string StatusText
        {
            get => _statusText;
            set => Set(ref _statusText, value);
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => Set(ref _selectedTabIndex, value);
        }

        public async Task InitializeAsync()
        {
            await Schedule.InitializeAsync();
            Dictionaries.LoadAll();
            Reports.LoadPeriods();
            Reports.Refresh();
            StatusText = "Приложение готово";
        }

        public void OnDataChanged(string source)
        {
            if (source != nameof(Schedule))
            {
                Schedule.RefreshCommand.Execute(null);
            }
            if (source != nameof(Reports))
            {
                Reports.LoadPeriods();
                Reports.Refresh();
            }
            if (source != nameof(Dictionaries) && source != nameof(Constraints))
            {
                Dictionaries.LoadAll();
            }
            StatusText = "Данные обновлены";
        }
    }
}
