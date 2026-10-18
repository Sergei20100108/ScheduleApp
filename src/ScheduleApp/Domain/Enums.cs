using System.ComponentModel;

namespace ScheduleApp.Domain
{
    public enum WeekDay
    {
        [Description("Понедельник")] Monday = 0,
        [Description("Вторник")] Tuesday = 1,
        [Description("Среда")] Wednesday = 2,
        [Description("Четверг")] Thursday = 3,
        [Description("Пятница")] Friday = 4,
        [Description("Суббота")] Saturday = 5,
        [Description("Воскресенье")] Sunday = 6
    }

    public enum AuditoriumType
    {
        [Description("Лекционная")] Lecture = 0,
        [Description("Лаборатория")] Laboratory = 1,
        [Description("Компьютерный класс")] ComputerClass = 2,
        [Description("Семинарская")] Seminar = 3,
        [Description("Спортивный зал")] Gym = 4,
        [Description("Мастерская")] Workshop = 5
    }

    public enum DisciplineType
    {
        [Description("Лекция")] Lecture = 0,
        [Description("Практика")] Practice = 1,
        [Description("Лабораторная")] Laboratory = 2,
        [Description("Семинар")] Seminar = 3,
        [Description("Физвоспитание")] PhysicalEducation = 4
    }

    public enum PreferenceKind
    {
        [Description("Запрещённый день")] ForbiddenDay = 0,
        [Description("Запрещённое время")] ForbiddenTime = 1,
        [Description("Предпочтительный день")] PreferredDay = 2,
        [Description("Предпочтительное время")] PreferredTime = 3,
        [Description("Предпочтительная аудитория")] PreferredAuditorium = 4,
        [Description("Нежелательная аудитория")] AvoidAuditorium = 5
    }

    public enum PeriodType
    {
        [Description("Неделя")] Week = 0,
        [Description("Месяц")] Month = 1,
        [Description("Семестр")] Semester = 2
    }

    public enum GenerationMode
    {
        [Description("Полная перегенерация")] Full = 0,
        [Description("Инкрементальная (сохранять заблокированные)")] Incremental = 1,
        [Description("Только дораспределить пустые слоты")] FillEmpty = 2,
        [Description("Только проверка ограничений")] ValidateOnly = 3
    }

    public enum ComplexityLevel
    {
        [Description("Авто")] Auto = 0,
        [Description("Быстрая")] Fast = 1,
        [Description("Сбалансированная")] Balanced = 2,
        [Description("Оптимальная")] Optimal = 3
    }

    public enum GridViewMode
    {
        [Description("Недельная")] Week = 0,
        [Description("Месячная")] Month = 1,
        [Description("По аудитории")] ByAuditorium = 2,
        [Description("По преподавателю")] ByTeacher = 3,
        [Description("Сводная")] Summary = 4
    }

    public enum GroupingMode
    {
        [Description("Без группировки")] None = 0,
        [Description("По преподавателям")] ByTeacher = 1,
        [Description("По аудиториям")] ByAuditorium = 2,
        [Description("По группам")] ByGroup = 3
    }

    public enum SummaryDimension
    {
        [Description("Группы")] Groups = 0,
        [Description("Преподаватели")] Teachers = 1,
        [Description("Аудитории")] Auditoriums = 2
    }

    public enum ThemeKind
    {
        [Description("Светлая")] Light = 0,
        [Description("Тёмная")] Dark = 1
    }
}
