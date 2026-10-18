using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ScheduleApp.Domain;
using ScheduleApp.Domain.Entities;

namespace ScheduleApp.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ScheduleDbContext db)
        {
            db.Database.EnsureCreated();
            if (db.Teachers.Any())
            {
                return;
            }
            Seed(db);
            db.SaveChanges();
        }

        private static void Seed(ScheduleDbContext db)
        {
            var rnd = new Random(20261017);

            var slots = new List<TimeSlot>();
            var starts = new[] { 480, 580, 680, 800, 900, 1000, 1100, 1200 };
            for (var i = 0; i < starts.Length; i++)
            {
                slots.Add(new TimeSlot
                {
                    Order = i + 1,
                    StartMinutes = starts[i],
                    EndMinutes = starts[i] + 90
                });
            }
            db.TimeSlots.AddRange(slots);

            var disciplines = new List<Discipline>
            {
                Disc("Математический анализ", DisciplineType.Lecture, "#4E79A7"),
                Disc("Математический анализ", DisciplineType.Practice, "#A0C4E8"),
                Disc("Линейная алгебра", DisciplineType.Lecture, "#59A14F"),
                Disc("Линейная алгебра", DisciplineType.Practice, "#B7E3A0"),
                Disc("Общая физика", DisciplineType.Lecture, "#F28E2B"),
                Disc("Общая физика", DisciplineType.Laboratory, "#FBD3A6"),
                Disc("Программирование", DisciplineType.Lecture, "#E15759"),
                Disc("Программирование", DisciplineType.Laboratory, "#F2B3B4"),
                Disc("Дискретная математика", DisciplineType.Lecture, "#B07AA1"),
                Disc("Дискретная математика", DisciplineType.Practice, "#DCB8E0"),
                Disc("Базы данных", DisciplineType.Lecture, "#76B7B2"),
                Disc("Базы данных", DisciplineType.Laboratory, "#BCE6DE"),
                Disc("Иностранный язык", DisciplineType.Seminar, "#EDC948"),
                Disc("Экономическая теория", DisciplineType.Lecture, "#BAB0AC"),
                Disc("Экономическая теория", DisciplineType.Seminar, "#D9D9D9"),
                Disc("Физическая культура", DisciplineType.PhysicalEducation, "#86BCB6"),
                Disc("Машинное обучение", DisciplineType.Lecture, "#FF9DA7"),
                Disc("Машинное обучение", DisciplineType.Laboratory, "#FFD3D8"),
                Disc("Веб-разработка", DisciplineType.Laboratory, "#9D7660"),
                Disc("Теория вероятностей", DisciplineType.Lecture, "#D37295")
            };
            db.Disciplines.AddRange(disciplines);

            var departments = new[]
            {
                "Кафедра высшей математики", "Кафедра общей физики",
                "Кафедра программирования", "Кафедра информатики",
                "Кафедра иностранных языков", "Кафедра экономики",
                "Кафедра физического воспитания", "Кафедра дискретной математики"
            };
            var lastNames = new[]
            {
                "Иванов", "Смирнов", "Кузнецов", "Попов", "Соколов", "Михайлов", "Новиков", "Фёдоров",
                "Морозов", "Волков", "Алексеев", "Лебедев", "Козлов", "Никитин", "Орлов", "Макаров",
                "Николаев", "Тихонов", "Зайцев", "Соколова", "Петрова", "Волкова", "Морозова", "Кузнецова",
                "Лебедева", "Павлов", "Семёнов", "Голубев", "Богданов", "Воробьёв", "Фомина", "Егорова",
                "Павлова", "Сергеева", "Кузьмин", "Шмидт", "Тарасов", "Белова", "Комаров", "Орлова"
            };
            var firstNames = new[]
            {
                "Александр", "Сергей", "Дмитрий", "Андрей", "Павел", "Виктор", "Илья", "Максим",
                "Елена", "Ольга", "Наталья", "Ирина", "Анна", "Мария", "Юрий", "Владимир"
            };
            var patr = new[] { "Игоревич", "Сергеевич", "Владимирович", "Андреевич", "Петрович", "Викторович", "Олеговна", "Александровна", "Ивановна", "Николаевна" };

            var teachers = new List<Teacher>();
            for (var i = 0; i < 40; i++)
            {
                teachers.Add(new Teacher
                {
                    FullName = $"{lastNames[i]} {firstNames[i % firstNames.Length]} {patr[(i * 3 + 1) % patr.Length]}",
                    Department = departments[i % departments.Length]
                });
            }
            db.Teachers.AddRange(teachers);

            var rooms = new List<Auditorium>();
            void AddRooms(int count, int startNumber, AuditoriumType type, int minCap, int maxCap, string building)
            {
                for (var i = 0; i < count; i++)
                {
                    rooms.Add(new Auditorium
                    {
                        Number = (startNumber + i).ToString(),
                        Type = type,
                        Capacity = rnd.Next(minCap, maxCap + 1),
                        Building = building
                    });
                }
            }
            AddRooms(12, 101, AuditoriumType.Lecture, 90, 240, "Главный корпус");
            AddRooms(18, 201, AuditoriumType.Laboratory, 14, 30, "Главный корпус");
            AddRooms(14, 301, AuditoriumType.ComputerClass, 15, 30, "Учебный корпус №2");
            AddRooms(10, 401, AuditoriumType.Seminar, 18, 40, "Учебный корпус №2");
            AddRooms(3, 501, AuditoriumType.Gym, 40, 60, "Спортивный корпус");
            AddRooms(3, 601, AuditoriumType.Workshop, 15, 25, "Учебный корпус №2");
            db.Auditoriums.AddRange(rooms);

            var specialties = new[] { "ИС", "ПМ", "ПО", "ЭК", "ФТ", "МХ" };
            var groups = new List<StudentGroup>();
            var groupNames = new List<string>();
            foreach (var spec in specialties)
            {
                for (var course = 1; course <= 4; course++)
                {
                    var perCourse = course == 1 ? 3 : 2;
                    for (var n = 1; n <= perCourse; n++)
                    {
                        var name = $"{spec}-{course}{n}";
                        groupNames.Add(name);
                        groups.Add(new StudentGroup
                        {
                            Name = name,
                            Course = course,
                            StudentCount = rnd.Next(14, 31)
                        });
                    }
                }
            }
            db.Groups.AddRange(groups);

            var periods = new List<SchedulePeriod>
            {
                new SchedulePeriod { Name = "Осень 2026 (семестр)", Type = PeriodType.Semester, StartDate = new DateTime(2026, 9, 1), DurationWeeks = 16 },
                new SchedulePeriod { Name = "Октябрь 2026", Type = PeriodType.Month, StartDate = new DateTime(2026, 10, 1), DurationWeeks = 4 },
                new SchedulePeriod { Name = "Текущая неделя", Type = PeriodType.Week, StartDate = new DateTime(2026, 10, 12), DurationWeeks = 1 }
            };
            db.Periods.AddRange(periods);

            // Промежуточный SaveChanges: генерирует Id для слотов, дисциплин, преподавателей,
            // аудиторий, групп и периодов, на которые ссылаются записи учебного плана и предпочтений.
            db.SaveChanges();

            // Учебный план: дисциплины по специальности и курсу
            Discipline D(string name, DisciplineType type) =>
                disciplines.First(x => x.Name == name && x.Type == type);

            var mathTeachers = teachers.Where(t => t.Department == departments[0]).ToList();
            var mathTeachers2 = teachers.Where(t => t.Department == departments[7]).ToList();
            var physTeachers = teachers.Where(t => t.Department == departments[1]).ToList();
            var progTeachers = teachers.Where(t => t.Department == departments[2]).ToList();
            var infTeachers = teachers.Where(t => t.Department == departments[3]).ToList();
            var langTeachers = teachers.Where(t => t.Department == departments[4]).ToList();
            var econTeachers = teachers.Where(t => t.Department == departments[5]).ToList();
            var physEdTeachers = teachers.Where(t => t.Department == departments[6]).ToList();

            var plan = new List<StudyPlanItem>();
            var pickIdx = 0;
            Teacher Pick(List<Teacher> list) => list[pickIdx++ % list.Count];

            foreach (var g in groups)
            {
                if (g.Course <= 2)
                {
                    plan.Add(Item(D("Математический анализ", DisciplineType.Lecture), Pick(mathTeachers), g, 1));
                    plan.Add(Item(D("Математический анализ", DisciplineType.Practice), Pick(mathTeachers), g, 2));
                    plan.Add(Item(D("Линейная алгебра", DisciplineType.Lecture), Pick(mathTeachers2), g, 1));
                    plan.Add(Item(D("Линейная алгебра", DisciplineType.Practice), Pick(mathTeachers2), g, 1));
                }
                else
                {
                    plan.Add(Item(D("Теория вероятностей", DisciplineType.Lecture), Pick(mathTeachers), g, 1));
                    plan.Add(Item(D("Дискретная математика", DisciplineType.Lecture), Pick(mathTeachers2), g, 1));
                    plan.Add(Item(D("Дискретная математика", DisciplineType.Practice), Pick(mathTeachers2), g, 1));
                }

                if (g.Course <= 3)
                {
                    plan.Add(Item(D("Общая физика", DisciplineType.Lecture), Pick(physTeachers), g, 1));
                    if (g.Course <= 2)
                        plan.Add(Item(D("Общая физика", DisciplineType.Laboratory), Pick(physTeachers), g, 1));
                }

                plan.Add(Item(D("Программирование", DisciplineType.Lecture), Pick(progTeachers), g, 1));
                plan.Add(Item(D("Программирование", DisciplineType.Laboratory), Pick(progTeachers), g, 2));

                if (g.Course >= 2)
                {
                    plan.Add(Item(D("Базы данных", DisciplineType.Lecture), Pick(infTeachers), g, 1));
                    plan.Add(Item(D("Базы данных", DisciplineType.Laboratory), Pick(infTeachers), g, 1));
                }
                if (g.Course >= 3 && (g.Name[0] == 'И' || g.Name[0] == 'П' || g.Name[0] == 'М'))
                {
                    plan.Add(Item(D("Машинное обучение", DisciplineType.Lecture), Pick(progTeachers), g, 1));
                    plan.Add(Item(D("Веб-разработка", DisciplineType.Laboratory), Pick(progTeachers), g, 1));
                }

                plan.Add(Item(D("Иностранный язык", DisciplineType.Seminar), Pick(langTeachers), g, 2));

                if (g.Course == 1 || g.Course == 2)
                {
                    plan.Add(Item(D("Экономическая теория", DisciplineType.Lecture), Pick(econTeachers), g, 1));
                    plan.Add(Item(D("Экономическая теория", DisciplineType.Seminar), Pick(econTeachers), g, 1));
                }

                plan.Add(Item(D("Физическая культура", DisciplineType.PhysicalEducation), Pick(physEdTeachers), g, 2));
            }
            db.StudyPlanItems.AddRange(plan);

            // Предпочтения преподавателей: у части — множественные пересекающиеся запреты
            var prefs = new List<TeacherPreference>();
            var allSlots = slots.Select(s => s.Id).ToList();
            var seminarRooms = rooms.Where(r => r.Type == AuditoriumType.Seminar).Select(r => r.Id).ToList();
            var lectureRooms = rooms.Where(r => r.Type == AuditoriumType.Lecture).Select(r => r.Id).ToList();

            for (var i = 0; i < teachers.Count; i++)
            {
                var t = teachers[i];
                var hardDay = (WeekDay)(i % 5);
                var hardSlot = allSlots[(i * 3 + 4) % allSlots.Count];

                // Жёсткий запрет дня
                prefs.Add(new TeacherPreference
                {
                    TeacherId = t.Id, Kind = PreferenceKind.ForbiddenDay, Day = hardDay,
                    Weight = 100, IsHard = true
                });
                // Жёсткий запрет времени, пересекающийся с предыдущим (множественные запреты)
                prefs.Add(new TeacherPreference
                {
                    TeacherId = t.Id, Kind = PreferenceKind.ForbiddenTime, TimeSlotId = hardSlot,
                    Weight = 100, IsHard = true
                });
                // Мягкий второй запрет дня
                prefs.Add(new TeacherPreference
                {
                    TeacherId = t.Id, Kind = PreferenceKind.ForbiddenDay, Day = (WeekDay)((i + 4) % 6),
                    Weight = 30 + (i % 4) * 10, IsHard = false
                });
                // Предпочтительное время
                prefs.Add(new TeacherPreference
                {
                    TeacherId = t.Id, Kind = PreferenceKind.PreferredTime, TimeSlotId = allSlots[(i * 2 + 1) % 4],
                    Weight = 60 + (i % 3) * 10, IsHard = false
                });
                // Предпочтительный день
                prefs.Add(new TeacherPreference
                {
                    TeacherId = t.Id, Kind = PreferenceKind.PreferredDay, Day = (WeekDay)(i % 4),
                    Weight = 40 + (i % 5) * 8, IsHard = false
                });
                // Предпочтительная аудитория (для лекционных кафедр)
                if (i % 3 == 0)
                {
                    prefs.Add(new TeacherPreference
                    {
                        TeacherId = t.Id, Kind = PreferenceKind.PreferredAuditorium,
                        AuditoriumId = lectureRooms[i % lectureRooms.Count],
                        Weight = 50, IsHard = false
                    });
                }
                if (i % 4 == 1)
                {
                    prefs.Add(new TeacherPreference
                    {
                        TeacherId = t.Id, Kind = PreferenceKind.PreferredAuditorium,
                        AuditoriumId = seminarRooms[i % seminarRooms.Count],
                        Weight = 45, IsHard = false
                    });
                }
            }
            db.TeacherPreferences.AddRange(prefs);

            db.SaveChanges();
        }

        private static Discipline Disc(string name, DisciplineType type, string color) =>
            new Discipline { Name = name, Type = type, ColorHex = color };

        private static StudyPlanItem Item(Discipline d, Teacher t, StudentGroup g, int perWeek) =>
            new StudyPlanItem
            {
                Discipline = d,
                Teacher = t,
                Group = g,
                LessonsPerWeek = perWeek
            };
    }
}
