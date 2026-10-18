using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ScheduleApp.Data;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;

namespace ScheduleApp.Services.Data
{
    public sealed class ScheduleSnapshot
    {
        public List<TimeSlot> Slots = new List<TimeSlot>();
        public List<Auditorium> Rooms = new List<Auditorium>();
        public List<Teacher> Teachers = new List<Teacher>();
        public List<StudentGroup> Groups = new List<StudentGroup>();
        public List<Discipline> Disciplines = new List<Discipline>();
        public List<StudyPlanItem> Plan = new List<StudyPlanItem>();
        public List<TeacherPreference> Preferences = new List<TeacherPreference>();
        public List<SchedulePeriod> Periods = new List<SchedulePeriod>();
        public List<Lesson> Lessons = new List<Lesson>();
    }

    public interface IScheduleDataService
    {
        ScheduleSnapshot Load(int periodId, bool includeLessons = true);
        void SaveLesson(Lesson lesson);
        void DeleteLesson(int lessonId);
        void DeleteLessons(IEnumerable<int> lessonIds);
        Lesson GetLesson(int lessonId);
    }

    public sealed class ScheduleDataService : IScheduleDataService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ScheduleDataService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public ScheduleSnapshot Load(int periodId, bool includeLessons = true)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                var snap = new ScheduleSnapshot
                {
                    Slots = db.TimeSlots.AsNoTracking().OrderBy(s => s.Order).ToList(),
                    Rooms = db.Auditoriums.AsNoTracking().OrderBy(r => r.Number).ToList(),
                    Teachers = db.Teachers.AsNoTracking().OrderBy(t => t.FullName).ToList(),
                    Groups = db.Groups.AsNoTracking().OrderBy(g => g.Name).ToList(),
                    Disciplines = db.Disciplines.AsNoTracking().OrderBy(d => d.Name).ToList(),
                    Plan = db.StudyPlanItems.AsNoTracking().ToList(),
                    Preferences = db.TeacherPreferences.AsNoTracking().ToList(),
                    Periods = db.Periods.AsNoTracking().OrderBy(p => p.StartDate).ToList()
                };
                if (includeLessons)
                {
                    snap.Lessons = db.Lessons.AsNoTracking()
                        .Include(l => l.LessonGroups)
                        .Include(l => l.Discipline)
                        .Include(l => l.Teacher)
                        .Include(l => l.Auditorium)
                        .Include(l => l.TimeSlot)
                        .Where(l => l.PeriodId == periodId)
                        .ToList();
                }
                return snap;
            }
        }

        public void SaveLesson(Lesson lesson)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                if (lesson.Id == 0)
                {
                    db.Lessons.Add(lesson);
                }
                else
                {
                    var stored = db.Lessons.Include(l => l.LessonGroups).First(l => l.Id == lesson.Id);
                    stored.Day = lesson.Day;
                    stored.TimeSlotId = lesson.TimeSlotId;
                    stored.AuditoriumId = lesson.AuditoriumId;
                    stored.TeacherId = lesson.TeacherId;
                    stored.DisciplineId = lesson.DisciplineId;
                    stored.StudyPlanItemId = lesson.StudyPlanItemId;
                    stored.IsLocked = lesson.IsLocked;
                    db.LessonGroups.RemoveRange(stored.LessonGroups);
                    foreach (var gid in lesson.LessonGroups.Select(g => g.GroupId))
                        db.LessonGroups.Add(new LessonGroup { LessonId = stored.Id, GroupId = gid });
                }
                db.SaveChanges();
            }
        }

        public Lesson GetLesson(int lessonId)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                return db.Lessons.AsNoTracking()
                    .Include(l => l.LessonGroups)
                    .Include(l => l.Discipline)
                    .Include(l => l.Teacher)
                    .Include(l => l.Auditorium)
                    .Include(l => l.TimeSlot)
                    .FirstOrDefault(l => l.Id == lessonId);
            }
        }

        public void DeleteLesson(int lessonId) => DeleteLessons(new[] { lessonId });

        public void DeleteLessons(IEnumerable<int> lessonIds)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ScheduleDbContext>();
                var ids = lessonIds.ToList();
                var lessons = db.Lessons.Include(l => l.LessonGroups).Where(l => ids.Contains(l.Id)).ToList();
                db.Lessons.RemoveRange(lessons);
                db.SaveChanges();
            }
        }
    }
}
