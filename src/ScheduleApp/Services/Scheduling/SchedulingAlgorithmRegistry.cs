using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ScheduleApp.Domain;

namespace ScheduleApp.Services.Scheduling
{
    /// <summary>
    /// Реестр стратегий: все классы, реализующие ISchedulingAlgorithm, обнаруживаются автоматически.
    /// Добавление нового алгоритма не требует правок UI — достаточно создать класс.
    /// </summary>
    public sealed class SchedulingAlgorithmRegistry
    {
        private readonly List<ISchedulingAlgorithm> _all;

        public SchedulingAlgorithmRegistry()
        {
            _all = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ISchedulingAlgorithm).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .Select(t => (ISchedulingAlgorithm)Activator.CreateInstance(t))
                .OrderBy(a => a.TypicalLevel)
                .ThenBy(a => a.DisplayName)
                .ToList();
        }

        public IReadOnlyList<ISchedulingAlgorithm> All => _all;

        public ISchedulingAlgorithm Resolve(ComplexityLevel level, int demandCount)
        {
            switch (level)
            {
                case ComplexityLevel.Fast:
                    return ById("greedy");
                case ComplexityLevel.Balanced:
                    return demandCount <= 600 ? ById("backtracking") : (ISchedulingAlgorithm)ById("hillclimbing");
                case ComplexityLevel.Optimal:
                    return demandCount <= 900 ? ById("annealing") : ById("genetic");
                default: // Авто
                    if (demandCount <= 250) return ById("backtracking");
                    if (demandCount <= 1000) return ById("annealing");
                    return ById("genetic");
            }
        }

        public ISchedulingAlgorithm ById(string id) =>
            _all.FirstOrDefault(a => a.Id == id) ?? _all[0];
    }
}
