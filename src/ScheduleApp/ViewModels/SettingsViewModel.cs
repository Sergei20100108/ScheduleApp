using System;
using System.Windows;
using ScheduleApp.Domain;
using ScheduleApp.Services.Settings;

namespace ScheduleApp.ViewModels
{
    /// <summary>Вкладка «Настройки»: тема, содержимое плиток, веса ограничений.</summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settings;
        private readonly Action _onSettingsChanged;

        public SettingsViewModel(ISettingsService settings, Action onSettingsChanged)
        {
            _settings = settings;
            _onSettingsChanged = onSettingsChanged;
            SaveCommand = new RelayCommand(Save);
            ResetCommand = new RelayCommand(Reset);
            LoadFromSettings();
        }

        public RelayCommand SaveCommand { get; }
        public RelayCommand ResetCommand { get; }

        public Array Themes => Enum.GetValues(typeof(ThemeKind));
        public Array Complexities => Enum.GetValues(typeof(ComplexityLevel));

        private ThemeKind _theme;
        public ThemeKind Theme
        {
            get => _theme;
            set => Set(ref _theme, value);
        }

        private bool _showTeacher;
        public bool ShowTeacherInTile
        {
            get => _showTeacher;
            set => Set(ref _showTeacher, value);
        }

        private bool _showGroups;
        public bool ShowGroupsInTile
        {
            get => _showGroups;
            set => Set(ref _showGroups, value);
        }

        private bool _showRoom;
        public bool ShowRoomInTile
        {
            get => _showRoom;
            set => Set(ref _showRoom, value);
        }

        private bool _highlightEmpty;
        public bool HighlightEmptyCells
        {
            get => _highlightEmpty;
            set => Set(ref _highlightEmpty, value);
        }

        private int _maxPairs;
        public int MaxPairsPerDay
        {
            get => _maxPairs;
            set => Set(ref _maxPairs, value);
        }

        private double _wDay;
        public double WeightPreferredDay
        {
            get => _wDay;
            set => Set(ref _wDay, value);
        }

        private double _wTime;
        public double WeightPreferredTime
        {
            get => _wTime;
            set => Set(ref _wTime, value);
        }

        private double _wRoom;
        public double WeightPreferredAuditorium
        {
            get => _wRoom;
            set => Set(ref _wRoom, value);
        }

        private double _wType;
        public double WeightRoomType
        {
            get => _wType;
            set => Set(ref _wType, value);
        }

        private double _wCapacity;
        public double WeightCapacity
        {
            get => _wCapacity;
            set => Set(ref _wCapacity, value);
        }

        private double _wPairs;
        public double WeightMaxPairs
        {
            get => _wPairs;
            set => Set(ref _wPairs, value);
        }

        private ComplexityLevel _defaultComplexity;
        public ComplexityLevel DefaultComplexity
        {
            get => _defaultComplexity;
            set => Set(ref _defaultComplexity, value);
        }

        private void LoadFromSettings()
        {
            var s = _settings.Current;
            Theme = s.Theme;
            ShowTeacherInTile = s.ShowTeacherInTile;
            ShowGroupsInTile = s.ShowGroupsInTile;
            ShowRoomInTile = s.ShowRoomInTile;
            HighlightEmptyCells = s.HighlightEmptyCells;
            MaxPairsPerDay = s.MaxPairsPerDay;
            WeightPreferredDay = s.WeightPreferredDay;
            WeightPreferredTime = s.WeightPreferredTime;
            WeightPreferredAuditorium = s.WeightPreferredAuditorium;
            WeightRoomType = s.WeightRoomType;
            WeightCapacity = s.WeightCapacity;
            WeightMaxPairs = s.WeightMaxPairs;
            DefaultComplexity = s.DefaultComplexity;
        }

        private void Save()
        {
            var s = _settings.Current;
            s.Theme = Theme;
            s.ShowTeacherInTile = ShowTeacherInTile;
            s.ShowGroupsInTile = ShowGroupsInTile;
            s.ShowRoomInTile = ShowRoomInTile;
            s.HighlightEmptyCells = HighlightEmptyCells;
            s.MaxPairsPerDay = Math.Max(1, MaxPairsPerDay);
            s.WeightPreferredDay = WeightPreferredDay;
            s.WeightPreferredTime = WeightPreferredTime;
            s.WeightPreferredAuditorium = WeightPreferredAuditorium;
            s.WeightRoomType = WeightRoomType;
            s.WeightCapacity = WeightCapacity;
            s.WeightMaxPairs = WeightMaxPairs;
            s.DefaultComplexity = DefaultComplexity;
            _settings.Save();
            ThemeManager.Apply(Theme);
            _onSettingsChanged?.Invoke();
        }

        private void Reset()
        {
            _settings.Current = new AppSettings();
            LoadFromSettings();
        }
    }

    /// <summary>Переключение тем приложения подменой словаря ресурсов.</summary>
    public static class ThemeManager
    {
        public static void Apply(ThemeKind kind)
        {
            var app = Application.Current;
            if (app == null) return;
            var uri = kind == ThemeKind.Dark
                ? new Uri("Resources/Themes/Dark.xaml", UriKind.Relative)
                : new Uri("Resources/Themes/Light.xaml", UriKind.Relative);

            var dicts = app.Resources.MergedDictionaries;
            for (var i = dicts.Count - 1; i >= 0; i--)
            {
                if (dicts[i].Source != null && dicts[i].Source.OriginalString.Contains("Themes/"))
                    dicts.RemoveAt(i);
            }
            dicts.Add(new ResourceDictionary { Source = uri });
        }
    }
}
