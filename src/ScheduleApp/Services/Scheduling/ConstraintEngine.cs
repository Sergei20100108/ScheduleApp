using System;
using System.Collections.Generic;
using System.Linq;
using ScheduleApp.Domain;

namespace ScheduleApp.Services.Scheduling
{
    /// <summary>
    /// Движок ограничений: единственное место, где определяются жёсткие/мягкие правила.
    /// Все алгоритмы обязаны использовать его проверки, что гарантирует ноль нарушений
    /// жёстких ограничений при любой стратегии.
    /// </summary>
    public sealed class ConstraintEngine
    {
        private readonly ScheduleProblem _p;
        private readonly Dictionary<int, HashSet<long>> _teacherBusy = new Dictionary<int, HashSet<long>>();
        private readonly Dictionary<int, HashSet<long>> _roomBusy = new Dictionary<int, HashSet<long>>();
        private readonly Dictionary<int, HashSet<long>> _groupBusy = new Dictionary<int, HashSet<long>>();
        private readonly Dictionary<int, int> _teacherDayLoad = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _groupDayLoad = new Dictionary<int, int>();
        private readonly Dictionary<int, Assignment> _byDemand = new Dictionary<int, Assignment>();
        private readonly List<string> _reasons = new List<string>();

        public double Score { get; private set; }
        public int PlacedCount => _byDemand.Count;

        public ConstraintEngine(ScheduleProblem problem)
        {
            _p = problem;
            foreach (var pre in problem.PreAssignments)
            {
                // Закреплённые/существующие занятия занимают ресурсы, но не являются «потребностями»
                // текущего расчёта. Для них синтезируется потребность-описатель, чтобы корректно
                // забронировать преподавателя, аудиторию и группы.
                Demand demand;
                if (pre.DemandId >= 0 && pre.DemandId < problem.Demands.Length)
                {
                    demand = problem.Demands[pre.DemandId];
                }
                else
                {
                    demand = new Demand
                    {
                        Id = -1,
                        PlanItemId = pre.PlanItemId,
                        TeacherId = pre.TeacherId,
                        GroupIds = pre.GroupIds ?? Array.Empty<int>(),
                        DisciplineType = (DisciplineType)pre.DisciplineType,
                        StudentCount = pre.StudentCount,
                        DisciplineName = "",
                        Label = "закреплённое занятие"
                    };
                }
                var a = new Assignment
                {
                    Demand = demand,
                    Day = pre.Day,
                    SlotId = pre.SlotId,
                    RoomId = pre.RoomId
                };
                Occupy(a);
                if (pre.DemandId >= 0)
                {
                    _byDemand[demand.Id] = a;
                }
            }
        }

        public Assignment GetAssignment(int demandId) =>
            _byDemand.TryGetValue(demandId, out var a) ? a : null;

        public IEnumerable<Assignment> AllAssignments => _byDemand.Values;

        private static long Key(int day, int slotId) => ((long)day << 32) ^ (uint)slotId;

        private static void AddBusy(Dictionary<int, HashSet<long>> map, int id, long key)
        {
            if (!map.TryGetValue(id, out var set))
            {
                set = new HashSet<long>();
                map[id] = set;
            }
            set.Add(key);
        }

        private static void RemoveBusy(Dictionary<int, HashSet<long>> map, int id, long key)
        {
            if (map.TryGetValue(id, out var set)) set.Remove(key);
        }

        private static void Inc(Dictionary<int, int> map, int id)
        {
            map.TryGetValue(id, out var v);
            map[id] = v + 1;
        }

        private static void Dec(Dictionary<int, int> map, int id)
        {
            if (!map.TryGetValue(id, out var v)) return;
            map[id] = Math.Max(0, v - 1);
        }

        private void Occupy(Assignment a)
        {
            var k = Key(a.Day, a.SlotId);
            AddBusy(_teacherBusy, a.Demand.TeacherId, k);
            AddBusy(_roomBusy, a.RoomId, k);
            foreach (var g in a.Demand.GroupIds) AddBusy(_groupBusy, g, k);
            Inc(_teacherDayLoad, a.Demand.TeacherId * 10 + a.Day);
            foreach (var g in a.Demand.GroupIds) Inc(_groupDayLoad, g * 10 + a.Day);
        }

        private void Release(Assignment a)
        {
            var k = Key(a.Day, a.SlotId);
            RemoveBusy(_teacherBusy, a.Demand.TeacherId, k);
            RemoveBusy(_roomBusy, a.RoomId, k);
            foreach (var g in a.Demand.GroupIds) RemoveBusy(_groupBusy, g, k);
            Dec(_teacherDayLoad, a.Demand.TeacherId * 10 + a.Day);
            foreach (var g in a.Demand.GroupIds) Dec(_groupDayLoad, g * 10 + a.Day);
        }

        public bool IsTeacherFree(int teacherId, int day, int slotId) =>
            !_teacherBusy.TryGetValue(teacherId, out var s) || !s.Contains(Key(day, slotId));

        public bool IsRoomFree(int roomId, int day, int slotId) =>
            !_roomBusy.TryGetValue(roomId, out var s) || !s.Contains(Key(day, slotId));

        public bool IsGroupFree(int groupId, int day, int slotId) =>
            !_groupBusy.TryGetValue(groupId, out var s) || !s.Contains(Key(day, slotId));

        public bool IsCellFree(int day, int slotId, int roomId, Demand demand)
        {
            if (!IsTeacherFree(demand.TeacherId, day, slotId)) return false;
            if (!IsRoomFree(roomId, day, slotId)) return false;
            foreach (var g in demand.GroupIds)
                if (!IsGroupFree(g, day, slotId)) return false;
            return true;
        }

        /// <summary>Проверка жёстких ограничений. Пустой список = разрешено.</summary>
        public IReadOnlyList<string> CheckHard(Demand demand, int day, int slotId, int roomId)
        {
            _reasons.Clear();
            var room = _p.Rooms.First(r => r.Id == roomId);

            if (!IsTeacherFree(demand.TeacherId, day, slotId))
                _reasons.Add($"преподаватель {_p.Teachers.First(t => t.Id == demand.TeacherId).FullName} уже занят в этот слот");
            if (!IsRoomFree(roomId, day, slotId))
                _reasons.Add($"аудитория {room.Number} уже занята");
            foreach (var g in demand.GroupIds)
                if (!IsGroupFree(g, day, slotId))
                    _reasons.Add($"группа {_p.Groups.First(x => x.Id == g).Name} уже имеет занятие в этот слот");

            if (_p.PreferencesByTeacher.TryGetValue(demand.TeacherId, out var prefs))
            {
                foreach (var pref in prefs.Where(x => x.IsHard))
                {
                    switch (pref.Kind)
                    {
                        case PreferenceKind.ForbiddenDay:
                            if (pref.Day.HasValue && (int)pref.Day.Value == day)
                                _reasons.Add("преподавателю запрещён этот день");
                            break;
                        case PreferenceKind.ForbiddenTime:
                            if (pref.SlotId == slotId)
                                _reasons.Add("преподавателю запрещено это время");
                            break;
                        case PreferenceKind.PreferredDay:
                            if (pref.Day.HasValue && (int)pref.Day.Value != day)
                                _reasons.Add("преподаватель может вести занятия только в этот день");
                            break;
                        case PreferenceKind.PreferredTime:
                            if (pref.SlotId != slotId)
                                _reasons.Add("преподаватель может вести занятия только в это время");
                            break;
                        case PreferenceKind.PreferredAuditorium:
                            if (pref.RoomId.HasValue && pref.RoomId.Value != roomId)
                                _reasons.Add("для преподавателя разрешена только выбранная аудитория");
                            break;
                        case PreferenceKind.AvoidAuditorium:
                            if (pref.RoomId == roomId)
                                _reasons.Add("аудитория запрещена преподавателю");
                            break;
                    }
                }
            }
            return _reasons.Count == 0 ? Array.Empty<string>() : _reasons.ToArray();
        }

        /// <summary>Мягкая оценка (чем больше, тем лучше; может быть отрицательной).</summary>
        public double ScorePlacement(Demand demand, int day, int slotId, int roomId, int teacherDayLoadAfter = -1, int groupDayLoadAfter = -1)
        {
            var w = _p.Weights;
            var s = 0.0;

            if (_p.PreferencesByTeacher.TryGetValue(demand.TeacherId, out var prefs))
            {
                foreach (var pref in prefs.Where(x => !x.IsHard))
                {
                    var matched = pref.Kind switch
                    {
                        PreferenceKind.ForbiddenDay => pref.Day.HasValue && (int)pref.Day.Value == day,
                        PreferenceKind.ForbiddenTime => pref.SlotId == slotId,
                        PreferenceKind.PreferredDay => pref.Day.HasValue && (int)pref.Day.Value == day,
                        PreferenceKind.PreferredTime => pref.SlotId == slotId,
                        PreferenceKind.PreferredAuditorium => pref.RoomId == roomId,
                        PreferenceKind.AvoidAuditorium => pref.RoomId == roomId,
                        _ => false
                    };
                    if (!matched) continue;
                    var scale = pref.Kind switch
                    {
                        PreferenceKind.PreferredDay => w.PreferredDay,
                        PreferenceKind.PreferredTime => w.PreferredTime,
                        PreferenceKind.PreferredAuditorium => w.PreferredAuditorium,
                        _ => w.SoftForbidden
                    };
                    var sign = IsPositiveKind(pref.Kind) ? 1.0 : -1.0;
                    s += sign * pref.Weight * scale;
                }
            }

            var room = _p.Rooms.First(r => r.Id == roomId);
            if (RoomMatches(demand.DisciplineType, (AuditoriumType)room.Type))
                s += 100 * w.RoomTypeMatch;
            else
                s -= 40 * w.RoomTypeMatch;

            if (room.Capacity >= demand.StudentCount)
                s += 100 * w.Capacity * Math.Min(1.0, (double)demand.StudentCount / room.Capacity + 0.35);
            else
                s -= 200 * w.Capacity * (1.0 - (double)room.Capacity / Math.Max(1, demand.StudentCount));

            var tLoad = teacherDayLoadAfter >= 0 ? teacherDayLoadAfter : TeacherDayLoad(demand.TeacherId, day) + 1;
            if (tLoad > _p.MaxPairsPerDay)
                s -= 100 * w.MaxPairsPerDay * (tLoad - _p.MaxPairsPerDay);

            foreach (var g in demand.GroupIds)
            {
                var gLoad = groupDayLoadAfter >= 0 ? groupDayLoadAfter : GroupDayLoad(g, day) + 1;
                if (gLoad > _p.MaxPairsPerDay)
                    s -= 100 * w.MaxPairsPerDay * (gLoad - _p.MaxPairsPerDay);
            }
            return s;
        }

        private static bool IsPositiveKind(PreferenceKind kind) =>
            kind == PreferenceKind.PreferredDay || kind == PreferenceKind.PreferredTime ||
            kind == PreferenceKind.PreferredAuditorium;

        public static bool RoomMatches(DisciplineType discipline, AuditoriumType room)
        {
            switch (discipline)
            {
                case DisciplineType.Lecture: return room == AuditoriumType.Lecture;
                case DisciplineType.Practice:
                case DisciplineType.Seminar: return room == AuditoriumType.Seminar || room == AuditoriumType.Lecture;
                case DisciplineType.Laboratory:
                    return room == AuditoriumType.Laboratory || room == AuditoriumType.ComputerClass || room == AuditoriumType.Workshop;
                case DisciplineType.PhysicalEducation: return room == AuditoriumType.Gym;
                default: return true;
            }
        }

        public int TeacherDayLoad(int teacherId, int day)
        {
            _teacherDayLoad.TryGetValue(teacherId * 10 + day, out var v);
            return v;
        }

        public int GroupDayLoad(int groupId, int day)
        {
            _groupDayLoad.TryGetValue(groupId * 10 + day, out var v);
            return v;
        }

        public bool Place(Assignment a)
        {
            if (CheckHard(a.Demand, a.Day, a.SlotId, a.RoomId).Count > 0) return false;
            Score += ScorePlacement(a.Demand, a.Day, a.SlotId, a.RoomId,
                TeacherDayLoad(a.Demand.TeacherId, a.Day) + 1,
                a.Demand.GroupIds.Length > 0 ? GroupDayLoad(a.Demand.GroupIds[0], a.Day) + 1 : -1);
            Occupy(a);
            _byDemand[a.Demand.Id] = a;
            return true;
        }

        public void ForcePlace(Assignment a)
        {
            Score += ScorePlacement(a.Demand, a.Day, a.SlotId, a.RoomId,
                TeacherDayLoad(a.Demand.TeacherId, a.Day) + 1,
                a.Demand.GroupIds.Length > 0 ? GroupDayLoad(a.Demand.GroupIds[0], a.Day) + 1 : -1);
            Occupy(a);
            _byDemand[a.Demand.Id] = a;
        }

        public void Unplace(int demandId)
        {
            if (!_byDemand.TryGetValue(demandId, out var a)) return;
            Score -= ScorePlacement(a.Demand, a.Day, a.SlotId, a.RoomId,
                TeacherDayLoad(a.Demand.TeacherId, a.Day),
                a.Demand.GroupIds.Length > 0 ? GroupDayLoad(a.Demand.GroupIds[0], a.Day) : -1);
            Release(a);
            _byDemand.Remove(demandId);
        }

        /// <summary>Пересчёт суммарного счёта (используется после внезапных мутаций).</summary>
        public void RecalculateScore()
        {
            Score = 0;
            foreach (var a in _byDemand.Values)
            {
                Score += ScorePlacement(a.Demand, a.Day, a.SlotId, a.RoomId,
                    TeacherDayLoad(a.Demand.TeacherId, a.Day),
                    a.Demand.GroupIds.Length > 0 ? GroupDayLoad(a.Demand.GroupIds[0], a.Day) : -1);
            }
        }

        /// <summary>Оценка верхнего предела для нормализации качества (0..100).</summary>
        public double ReferenceScore(int demandCount)
        {
            var prefPart = 0.0;
            foreach (var kv in _p.PreferencesByTeacher)
            {
                foreach (var pref in kv.Value.Where(x => !x.IsHard))
                {
                    var scale = pref.Kind switch
                    {
                        PreferenceKind.PreferredDay => _p.Weights.PreferredDay,
                        PreferenceKind.PreferredTime => _p.Weights.PreferredTime,
                        PreferenceKind.PreferredAuditorium => _p.Weights.PreferredAuditorium,
                        _ => _p.Weights.SoftForbidden
                    };
                    prefPart += pref.Weight * scale;
                }
            }
            var avgPrefPerTeacher = _p.Teachers.Length > 0 ? prefPart / _p.Teachers.Length : 0;
            var basePerDemand = avgPrefPerTeacher + 100 * _p.Weights.RoomTypeMatch + 100 * _p.Weights.Capacity;
            return Math.Max(1, basePerDemand) * demandCount;
        }
    }
}
