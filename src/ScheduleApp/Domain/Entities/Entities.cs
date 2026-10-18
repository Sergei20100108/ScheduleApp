using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ScheduleApp.Domain.Entities
{
    public class Auditorium
    {
        public int Id { get; set; }
        public string Number { get; set; }
        public int Capacity { get; set; }
        public AuditoriumType Type { get; set; }
        public string Building { get; set; }

        public override string ToString() => Number;
    }

    public class Teacher
    {
        public int Id { get; set; }
        public string FullName { get; set; }
        public string Department { get; set; }

        public ICollection<TeacherPreference> Preferences { get; set; } = new List<TeacherPreference>();

        public override string ToString() => FullName;
    }

    public class Discipline
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DisciplineType Type { get; set; }
        public string ColorHex { get; set; }

        public override string ToString() => Name;
    }

    public class StudentGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Course { get; set; }
        public int StudentCount { get; set; }

        public override string ToString() => Name;
    }

    public class TimeSlot
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public int StartMinutes { get; set; }
        public int EndMinutes { get; set; }

        [NotMapped]
        public string Display => $"{MinutesToString(StartMinutes)} – {MinutesToString(EndMinutes)}";

        public static string MinutesToString(int minutes) =>
            $"{minutes / 60:00}:{minutes % 60:00}";

        public override string ToString() => Display;
    }

    public class SchedulePeriod
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public PeriodType Type { get; set; }
        public System.DateTime StartDate { get; set; }
        public int DurationWeeks { get; set; }

        public override string ToString() => Name;
    }

    public class StudyPlanItem
    {
        public int Id { get; set; }
        public int DisciplineId { get; set; }
        public Discipline Discipline { get; set; }
        public int TeacherId { get; set; }
        public Teacher Teacher { get; set; }
        public int GroupId { get; set; }
        public StudentGroup Group { get; set; }
        public int LessonsPerWeek { get; set; }
    }

    public class TeacherPreference
    {
        public int Id { get; set; }
        public int TeacherId { get; set; }
        public Teacher Teacher { get; set; }
        public PreferenceKind Kind { get; set; }
        public WeekDay? Day { get; set; }
        public int? TimeSlotId { get; set; }
        public TimeSlot TimeSlot { get; set; }
        public int? AuditoriumId { get; set; }
        public Auditorium Auditorium { get; set; }
        public int Weight { get; set; } = 50;
        public bool IsHard { get; set; }
    }

    public class Lesson
    {
        public int Id { get; set; }
        public int PeriodId { get; set; }
        public SchedulePeriod Period { get; set; }
        public WeekDay Day { get; set; }
        public int TimeSlotId { get; set; }
        public TimeSlot TimeSlot { get; set; }
        public int AuditoriumId { get; set; }
        public Auditorium Auditorium { get; set; }
        public int TeacherId { get; set; }
        public Teacher Teacher { get; set; }
        public int DisciplineId { get; set; }
        public Discipline Discipline { get; set; }
        public int? StudyPlanItemId { get; set; }
        public bool IsLocked { get; set; }
        public ICollection<LessonGroup> LessonGroups { get; set; } = new List<LessonGroup>();
    }

    public class LessonGroup
    {
        public int LessonId { get; set; }
        public Lesson Lesson { get; set; }
        public int GroupId { get; set; }
        public StudentGroup Group { get; set; }
    }
}
