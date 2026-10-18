using System;
using System.Collections.Generic;
using System.Linq;

namespace ScheduleApp.Services.Scheduling
{
    public sealed class SlotInfo
    {
        public int Id;
        public int Order;
        public int StartMinutes;
        public int EndMinutes;
    }

    public sealed class RoomInfo
    {
        public int Id;
        public string Number;
        public int Capacity;
        public int Type;
    }

    public sealed class TeacherInfo
    {
        public int Id;
        public string FullName;
    }

    public sealed class GroupInfo
    {
        public int Id;
        public string Name;
        public int StudentCount;
    }

    public sealed class PreferenceInfo
    {
        public int Id;
        public Domain.PreferenceKind Kind;
        public Domain.WeekDay? Day;
        public int? SlotId;
        public int? RoomId;
        public int Weight;
        public bool IsHard;
    }

    public sealed class Demand
    {
        public int Id;
        public int? PlanItemId;
        public int DisciplineId;
        public string DisciplineName;
        public Domain.DisciplineType DisciplineType;
        public string DisciplineColor;
        public int TeacherId;
        public int[] GroupIds;
        public int StudentCount;
        public string Label;
    }

    public sealed class PreAssignment
    {
        public int DemandId; // -1 — занятие вне учебного плана (занятые ресурсы)
        public int Day;
        public int SlotId;
        public int RoomId;
        public bool IsLocked;
        // Данные для корректного «занятия» ресурсов (преподаватель/группы) без привязки к потребности плана.
        public int TeacherId;
        public int[] GroupIds = Array.Empty<int>();
        public int DisciplineType;
        public int StudentCount;
        public int? PlanItemId;
    }

    public sealed class SchedulingWeights
    {
        public double PreferredDay = 1.0;
        public double PreferredTime = 1.0;
        public double PreferredAuditorium = 0.6;
        public double SoftForbidden = 1.0;
        public double RoomTypeMatch = 0.30;
        public double Capacity = 0.20;
        public double MaxPairsPerDay = 1.2;
    }

    public sealed class ScheduleProblem
    {
        public SlotInfo[] Slots;
        public RoomInfo[] Rooms;
        public TeacherInfo[] Teachers;
        public GroupInfo[] Groups;
        public Demand[] Demands;
        public Dictionary<int, List<PreferenceInfo>> PreferencesByTeacher;
        public SchedulingWeights Weights;
        public int MaxPairsPerDay;
        public PreAssignment[] PreAssignments;
        public Domain.WeekDay[] AllowedDays;

        public int CellCount => AllowedDays.Length * Slots.Length;
    }

    public struct CellRef : IEquatable<CellRef>
    {
        public int Day;
        public int SlotId;
        public CellRef(int day, int slotId) { Day = day; SlotId = slotId; }
        public bool Equals(CellRef other) => Day == other.Day && SlotId == other.SlotId;
        public override bool Equals(object obj) => obj is CellRef c && Equals(c);
        public override int GetHashCode() => (Day * 397) ^ SlotId;
        public long Key => ((long)Day << 32) ^ (uint)SlotId;
    }

    public sealed class Assignment
    {
        public Demand Demand;
        public int Day;
        public int SlotId;
        public int RoomId;
    }

    public sealed class SchedulingParams
    {
        public int TimeBudgetMs = 20000;
        public int RandomSeed = Environment.TickCount;
    }

    public sealed class SolveProgress
    {
        public int Percent;
        public string Message;
        public double Score;
    }

    public sealed class SolveResult
    {
        public List<Assignment> Assignments = new List<Assignment>();
        public List<Demand> Unplaced = new List<Demand>();
        public double Score;
        public int Iterations;
        public string AlgorithmName;
        public TimeSpan Elapsed;
    }

    public interface ISchedulingAlgorithm
    {
        string Id { get; }
        string DisplayName { get; }
        string Description { get; }
        Domain.ComplexityLevel TypicalLevel { get; }
        SolveResult Solve(ScheduleProblem problem, SchedulingParams parameters,
            IProgress<SolveProgress> progress, System.Threading.CancellationToken ct);
    }
}
