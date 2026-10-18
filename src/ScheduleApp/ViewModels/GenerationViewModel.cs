using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;
using ScheduleApp.Services.Data;
using ScheduleApp.Services.Dialogs;
using ScheduleApp.Services.Scheduling;

namespace ScheduleApp.ViewModels
{
    /// <summary>Вкладка «Генерация»: выбор режима, запуск в фоне, прогресс, отмена, результат.</summary>
    public sealed class GenerationViewModel : ObservableObject
    {
        private readonly IGenerationService _generation;
        private readonly IScheduleDataService _data;
        private readonly IDialogService _dialogs;
        private readonly SchedulingAlgorithmRegistry _registry;
        private readonly Action _onDataChanged;

        private NamedItem<SchedulePeriod> _selectedPeriod;
        private ComplexityLevel _complexity = ComplexityLevel.Auto;
        private GenerationMode _mode = GenerationMode.Full;
        private bool _isRunning;
        private int _progressPercent;
        private string _progressMessage = "Готов к запуску";
        private string _resultSummary = "";
        private CancellationTokenSource _cts;
        private string _algorithmInfo = "";

        public GenerationViewModel(IGenerationService generation, IScheduleDataService data,
            IDialogService dialogs, SchedulingAlgorithmRegistry registry, Action onDataChanged)
        {
            _generation = generation;
            _data = data;
            _dialogs = dialogs;
            _registry = registry;
            _onDataChanged = onDataChanged;

            StartCommand = new RelayCommand(async () => await StartAsync(), () => !IsRunning && SelectedPeriod != null);
            CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);

            foreach (var p in _data.Load(0, includeLessons: false).Periods)
                Periods.Add(new NamedItem<SchedulePeriod> { Value = p, Name = $"{p.Name} ({p.StartDate:dd.MM.yyyy}, {p.DurationWeeks} нед.)" });
            SelectedPeriod = Periods.FirstOrDefault();
            UpdateAlgorithmInfo();
        }

        public RelayCommand StartCommand { get; }
        public RelayCommand CancelCommand { get; }

        public ObservableCollection<NamedItem<SchedulePeriod>> Periods { get; } = new ObservableCollection<NamedItem<SchedulePeriod>>();
        public ObservableCollection<string> Log { get; } = new ObservableCollection<string>();

        public Array Complexities => Enum.GetValues(typeof(ComplexityLevel));
        public Array Modes => Enum.GetValues(typeof(GenerationMode));

        public string ModeDescriptions =>
            "Полная перегенерация — удаляются все занятия, кроме заблокированных (IsLocked), и распределяется всё заново.\n" +
            "Инкрементальная — текущие занятия остаются на местах, добавляются только недостающие.\n" +
            "Только пустые слоты — быстрая жадная добивка без перестановок.\n" +
            "Только проверка — анализ ограничений и качества без изменений.";

        public NamedItem<SchedulePeriod> SelectedPeriod
        {
            get => _selectedPeriod;
            set => Set(ref _selectedPeriod, value);
        }

        public ComplexityLevel Complexity
        {
            get => _complexity;
            set { if (Set(ref _complexity, value)) UpdateAlgorithmInfo(); }
        }

        public GenerationMode Mode
        {
            get => _mode;
            set => Set(ref _mode, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { if (Set(ref _isRunning, value)) { Raise(nameof(IsIdle)); } }
        }

        public bool IsIdle => !_isRunning;

        public int ProgressPercent
        {
            get => _progressPercent;
            set => Set(ref _progressPercent, value);
        }

        public string ProgressMessage
        {
            get => _progressMessage;
            set => Set(ref _progressMessage, value);
        }

        public string ResultSummary
        {
            get => _resultSummary;
            set => Set(ref _resultSummary, value);
        }

        public string AlgorithmInfo
        {
            get => _algorithmInfo;
            set => Set(ref _algorithmInfo, value);
        }

        private void UpdateAlgorithmInfo()
        {
            var demandEstimate = 700; // ориентировочно для типового набора 60 групп
            var algo = _registry.Resolve(_complexity, demandEstimate);
            AlgorithmInfo = $"{algo.DisplayName}: {algo.Description}";
        }

        private async Task StartAsync()
        {
            if (SelectedPeriod == null) return;
            if (Mode == GenerationMode.Full || Mode == GenerationMode.Incremental)
            {
                var question = Mode == GenerationMode.Full
                    ? "Полная перегенерация удалит все незаблокированные занятия выбранного периода и распределит их заново. Продолжить?"
                    : "Инкрементальная генерация добавит недостающие занятия, не трогая существующие. Продолжить?";
                if (!_dialogs.Confirm(question, "Запуск генерации")) return;
            }

            IsRunning = true;
            _cts = new CancellationTokenSource();
            Log.Clear();
            ResultSummary = "";
            ProgressPercent = 0;
            ProgressMessage = "Подготовка…";

            var progress = new Progress<GenerationProgress>(p =>
            {
                ProgressPercent = p.Percent;
                ProgressMessage = p.Message;
                if (Log.Count == 0 || Log[Log.Count - 1] != p.Message)
                    Log.Add($"[{DateTime.Now:HH:mm:ss}] {p.Message}");
            });

            try
            {
                var report = await _generation.GenerateAsync(new GenerationRequest
                {
                    PeriodId = SelectedPeriod.Value.Id,
                    Mode = Mode,
                    Complexity = Complexity
                }, progress, _cts.Token);

                ResultSummary = report.Success
                    ? $"Алгоритм: {report.AlgorithmName}\n" +
                      $"Потребностей: {report.TotalDemands}, распределено: {report.Placed}, не распределено: {report.Unplaced}\n" +
                      $"Заблокированных сохранено: {report.LockedKept}\n" +
                      $"Соблюдение предпочтений: {report.QualityPercent:F1}%\n" +
                      $"Нарушений жёстких ограничений: {report.HardViolations}\n" +
                      $"Время: {report.Elapsed.TotalSeconds:F1} с\n{report.Message}"
                    : report.Message;

                if (report.UnplacedLabels.Count > 0)
                    Log.Add("Не распределено: " + string.Join("; ", report.UnplacedLabels.Take(10)));

                if (report.Success && Mode != GenerationMode.ValidateOnly)
                    _onDataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ResultSummary = $"Ошибка: {ex.Message}";
                _dialogs.Error($"Ошибка генерации: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}
