using System;
using System.IO;
using Newtonsoft.Json;
using ScheduleApp.Domain;
using ScheduleApp.Services.Scheduling;

namespace ScheduleApp.Services.Settings
{
    public sealed class AppSettings
    {
        public ThemeKind Theme { get; set; } = ThemeKind.Light;
        public bool ShowTeacherInTile { get; set; } = true;
        public bool ShowGroupsInTile { get; set; } = true;
        public bool ShowRoomInTile { get; set; } = true;
        public bool HighlightEmptyCells { get; set; } = true;
        public int MaxPairsPerDay { get; set; } = 5;
        public double WeightPreferredDay { get; set; } = 1.0;
        public double WeightPreferredTime { get; set; } = 1.0;
        public double WeightPreferredAuditorium { get; set; } = 0.6;
        public double WeightRoomType { get; set; } = 0.30;
        public double WeightCapacity { get; set; } = 0.20;
        public double WeightMaxPairs { get; set; } = 1.2;
        public ComplexityLevel DefaultComplexity { get; set; } = ComplexityLevel.Auto;

        public SchedulingWeights ToWeights() => new SchedulingWeights
        {
            PreferredDay = WeightPreferredDay,
            PreferredTime = WeightPreferredTime,
            PreferredAuditorium = WeightPreferredAuditorium,
            SoftForbidden = 1.0,
            RoomTypeMatch = WeightRoomType,
            Capacity = WeightCapacity,
            MaxPairsPerDay = WeightMaxPairs
        };
    }

    public interface ISettingsService
    {
        AppSettings Current { get; set; }
        void Save();
        void Reload();
    }

    public sealed class JsonSettingsService : ISettingsService
    {
        private static readonly string FilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public AppSettings Current { get; set; } = new AppSettings();

        public JsonSettingsService()
        {
            Reload();
        }

        public void Reload()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    Current = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                Current = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
            }
            catch
            {
                // Не роняем приложение из-за ошибки записи настроек
            }
        }
    }
}
