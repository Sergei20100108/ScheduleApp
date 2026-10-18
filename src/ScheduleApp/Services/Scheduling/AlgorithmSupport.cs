using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ScheduleApp.Domain;

namespace ScheduleApp.Services.Scheduling
{
    public struct Candidate
    {
        public int Day;
        public int SlotId;
        public int RoomId;
        public double Score;
    }

    /// <summary>Общая инфраструктура для стратегий генерации.</summary>
    public static class AlgorithmSupport
    {
        /// <summary>Отбор кандидатов (день, слот, аудитория) с сортировкой по убыванию оценки.</summary>
        public static List<Candidate> BuildCandidates(ConstraintEngine engine, ScheduleProblem p, Demand demand, int topK)
        {
            var rooms = SelectRooms(p, demand);
            var list = new List<Candidate>(64);
            foreach (var day in p.AllowedDays)
            {
                foreach (var slot in p.Slots)
                {
                    foreach (var room in rooms)
                    {
                        var d = (int)day;
                        var s = slot.Id;
                        if (!engine.IsCellFree(d, s, room.Id, demand)) continue;
                        if (engine.CheckHard(demand, d, s, room.Id).Count > 0) continue;
                        list.Add(new Candidate
                        {
                            Day = d, SlotId = s, RoomId = room.Id,
                            Score = engine.ScorePlacement(demand, d, s, room.Id)
                        });
                    }
                }
            }
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (topK > 0 && list.Count > topK) list.RemoveRange(topK, list.Count - topK);
            return list;
        }

        /// <summary>Аудитории, которые разумно рассматривать: вмещающие группу + ближайшие по вместимости.</summary>
        public static List<RoomInfo> SelectRooms(ScheduleProblem p, Demand demand)
        {
            var fitting = new List<RoomInfo>();
            var near = new List<RoomInfo>();
            foreach (var r in p.Rooms)
            {
                if (r.Capacity >= demand.StudentCount) fitting.Add(r);
                else if (r.Capacity >= demand.StudentCount * 0.6) near.Add(r);
            }
            fitting.Sort((a, b) => a.Capacity.CompareTo(b.Capacity));
            near.Sort((a, b) => b.Capacity.CompareTo(a.Capacity));
            var result = fitting.Take(12).ToList();
            result.AddRange(near.Take(2));
            return result;
        }

        /// <summary>Порядок расстановки: самые «трудные» потребности первыми.</summary>
        public static List<Demand> OrderDemands(ScheduleProblem p, Random rnd)
        {
            return p.Demands
                .Select(d => new { D = d, Key = DifficultyKey(p, d) })
                .OrderByDescending(x => x.Key + rnd.NextDouble() * 25.0)
                .Select(x => x.D)
                .ToList();
        }

        private static double DifficultyKey(ScheduleProblem p, Demand d)
        {
            var k = d.GroupIds.Length * 10.0;
            if (p.PreferencesByTeacher.TryGetValue(d.TeacherId, out var prefs))
            {
                k += prefs.Count(x => x.IsHard) * 12.0;
                k += prefs.Count * 2.0;
            }
            k += d.StudentCount * 0.1;
            return k;
        }

        public static double QualityPercent(ConstraintEngine engine, int placed) =>
            placed <= 0 ? 100 : Math.Max(0, Math.Min(100, 100.0 * engine.Score / engine.ReferenceScore(placed)));

        public static void Report(IProgress<SolveProgress> progress, Stopwatch sw, int total, int done, double score, string message)
        {
            if (progress == null) return;
            var pct = total > 0 ? (int)Math.Min(99, done * 100.0 / total) : 99;
            progress.Report(new SolveProgress { Percent = pct, Message = message, Score = score });
        }
    }
}
