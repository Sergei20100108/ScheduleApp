using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ScheduleApp.Domain;

namespace ScheduleApp.Services.Scheduling.Algorithms
{
    /// <summary>Жадный алгоритм: расставляет потребности по убыванию «трудности», выбирая лучшую допустимую ячейку.</summary>
    public sealed class GreedyAlgorithm : ISchedulingAlgorithm
    {
        public string Id => "greedy";
        public string DisplayName => "Жадный";
        public string Description => "Быстрое жадное распределение: каждое занятие сразу ставится в лучшую доступную ячейку. Самый скоростной вариант.";
        public ComplexityLevel TypicalLevel => ComplexityLevel.Fast;

        public SolveResult Solve(ScheduleProblem problem, SchedulingParams parameters,
            IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var rnd = new Random(parameters.RandomSeed);
            var engine = new ConstraintEngine(problem);
            var result = new SolveResult { AlgorithmName = DisplayName };
            var ordered = AlgorithmSupport.OrderDemands(problem, rnd);
            var done = 0;

            foreach (var demand in ordered)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                if (engine.GetAssignment(demand.Id) != null) continue;

                var candidates = AlgorithmSupport.BuildCandidates(engine, problem, demand, 0);
                if (candidates.Count > 0)
                {
                    var best = candidates[0];
                    engine.ForcePlace(new Assignment { Demand = demand, Day = best.Day, SlotId = best.SlotId, RoomId = best.RoomId });
                }
                else
                {
                    result.Unplaced.Add(demand);
                }

                if (done % 25 == 0)
                    AlgorithmSupport.Report(progress, sw, ordered.Count, done, engine.Score, "Распределение занятий (жадный)…");
            }

            result.Assignments.AddRange(engine.AllAssignments);
            result.Score = engine.Score;
            result.Iterations = ordered.Count;
            result.Elapsed = sw.Elapsed;
            progress?.Report(new SolveProgress { Percent = 100, Message = "Жадное распределение завершено", Score = engine.Score });
            return result;
        }
    }

    /// <summary>Жадный алгоритм с ограниченым бэктрекингом: при тупике возвращает назначения и пробует альтернативы.</summary>
    public sealed class BacktrackingAlgorithm : ISchedulingAlgorithm
    {
        public string Id => "backtracking";
        public string DisplayName => "Жадный с бэктрекингом";
        public string Description => "Жадная расстановка с возвратом: если занятие некуда поставить, алгоритм пересматривает предыдущие решения. Существенно реже оставляет «дыры».";
        public ComplexityLevel TypicalLevel => ComplexityLevel.Balanced;

        private const int TopK = 10;
        private const int MaxBacktracks = 4000;

        public SolveResult Solve(ScheduleProblem problem, SchedulingParams parameters,
            IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var rnd = new Random(parameters.RandomSeed);
            var ordered = AlgorithmSupport.OrderDemands(problem, rnd);
            var candCache = new Dictionary<int, List<Candidate>>();
            var attempt = 0;

            List<Candidate> Cands(Demand d)
            {
                if (!candCache.TryGetValue(d.Id, out var list))
                {
                    // «Свежие» кандидаты вычисляются по текущему состоянию движка — используем отдельный движок попытки
                    list = null;
                }
                return list;
            }

            SolveResult best = null;
            double bestScore = double.NegativeInfinity;

            while (sw.ElapsedMilliseconds < parameters.TimeBudgetMs)
            {
                ct.ThrowIfCancellationRequested();
                attempt++;
                var engine = new ConstraintEngine(problem);
                candCache.Clear();
                var stack = new List<(Demand demand, int candIndex, List<Candidate> cands)>();
                var backtracks = 0;
                var idx = 0;
                var failed = false;

                while (idx < ordered.Count)
                {
                    ct.ThrowIfCancellationRequested();
                    var demand = ordered[idx];
                    if (engine.GetAssignment(demand.Id) != null) { idx++; continue; }

                    var cands = AlgorithmSupport.BuildCandidates(engine, problem, demand, TopK);
                    if (cands.Count > 0)
                    {
                        var pick = SelectIndex(cands.Count, rnd);
                        var c = cands[pick];
                        engine.ForcePlace(new Assignment { Demand = demand, Day = c.Day, SlotId = c.SlotId, RoomId = c.RoomId });
                        stack.Add((demand, pick, cands));
                        idx++;
                        continue;
                    }

                    // Тупик: откатываемся
                    var retreated = false;
                    while (stack.Count > 0 && backtracks < MaxBacktracks)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (pd, pi, pc) = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        engine.Unplace(pd.Id);
                        idx = ordered.IndexOf(pd);
                        backtracks++;
                        var next = -1;
                        for (var j = pi + 1; j < pc.Count; j++)
                        {
                            if (engine.IsCellFree(pc[j].Day, pc[j].SlotId, pc[j].RoomId, pd) &&
                                engine.CheckHard(pd, pc[j].Day, pc[j].SlotId, pc[j].RoomId).Count == 0)
                            { next = j; break; }
                        }
                        if (next >= 0)
                        {
                            var c = pc[next];
                            engine.ForcePlace(new Assignment { Demand = pd, Day = c.Day, SlotId = c.SlotId, RoomId = c.RoomId });
                            stack.Add((pd, next, pc));
                            idx++;
                            retreated = true;
                            break;
                        }
                    }
                    if (!retreated) { failed = true; break; }
                }

                var unplacedCount = ordered.Count - engine.PlacedCount;
                if (!failed && unplacedCount == 0)
                {
                    var result = new SolveResult
                    {
                        AlgorithmName = DisplayName,
                        Score = engine.Score,
                        Iterations = attempt,
                        Elapsed = sw.Elapsed
                    };
                    result.Assignments.AddRange(engine.AllAssignments);
                    progress?.Report(new SolveProgress { Percent = 100, Message = "Все занятия распределены", Score = engine.Score });
                    return result;
                }

                if (engine.Score > bestScore)
                {
                    bestScore = engine.Score;
                    best = new SolveResult { Score = engine.Score, Iterations = attempt, Elapsed = sw.Elapsed };
                    best.Assignments.AddRange(engine.AllAssignments);
                    best.Unplaced = ordered.Where(d => engine.GetAssignment(d.Id) == null).ToList();
                }

                AlgorithmSupport.Report(progress, sw, 100, (int)(sw.ElapsedMilliseconds * 100.0 / parameters.TimeBudgetMs),
                    engine.Score, $"Попытка {attempt}: распределено {engine.PlacedCount}/{ordered.Count}…");
            }

            ct.ThrowIfCancellationRequested();
            var fallback = best ?? new SolveResult { AlgorithmName = DisplayName, Elapsed = sw.Elapsed };
            fallback.AlgorithmName = DisplayName;
            return fallback;
        }

        private static int SelectIndex(int count, Random rnd) =>
            count <= 3 || rnd.NextDouble() < 0.75 ? 0 : rnd.Next(Math.Min(count, 4));
    }

    /// <summary>Hill climbing: жадная инициализация, затем локальные улучшения (перенос занятий).</summary>
    public sealed class HillClimbingAlgorithm : ISchedulingAlgorithm
    {
        public string Id => "hillclimbing";
        public string DisplayName => "Локальный поиск (hill climbing)";
        public string Description => "Сначала строится жадное расписание, затем занятия переносятся в более подходящие ячейки, пока качество растёт. Всегда завершается результатом не хуже жадного.";
        public ComplexityLevel TypicalLevel => ComplexityLevel.Balanced;

        public SolveResult Solve(ScheduleProblem problem, SchedulingParams parameters,
            IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var rnd = new Random(parameters.RandomSeed);
            var engine = LocalSearchBase.BuildGreedy(problem, rnd, progress, ct);
            var demands = problem.Demands.Where(d => engine.GetAssignment(d.Id) != null).ToList();
            var improvements = 0;
            var idleRounds = 0;

            while (sw.ElapsedMilliseconds < parameters.TimeBudgetMs && idleRounds < 3)
            {
                ct.ThrowIfCancellationRequested();
                var improvedThisRound = false;

                foreach (var demand in demands.Shuffled(rnd))
                {
                    ct.ThrowIfCancellationRequested();
                    var current = engine.GetAssignment(demand.Id);
                    if (current == null) continue;
                    var currentScore = engine.ScorePlacement(demand, current.Day, current.SlotId, current.RoomId);

                    engine.Unplace(demand.Id);
                    var best = (Candidate?)null;
                    var bestGain = 0.0;
                    foreach (var c in AlgorithmSupport.BuildCandidates(engine, problem, demand, 24))
                    {
                        var gain = c.Score - currentScore;
                        if (gain > bestGain) { bestGain = gain; best = c; }
                    }

                    if (best.HasValue)
                    {
                        var b = best.Value;
                        engine.ForcePlace(new Assignment { Demand = demand, Day = b.Day, SlotId = b.SlotId, RoomId = b.RoomId });
                        improvements++;
                        improvedThisRound = true;
                    }
                    else
                    {
                        engine.ForcePlace(current);
                    }
                }

                idleRounds = improvedThisRound ? 0 : idleRounds + 1;
                AlgorithmSupport.Report(progress, sw, 100, (int)(sw.ElapsedMilliseconds * 100.0 / parameters.TimeBudgetMs),
                    engine.Score, $"Локальный поиск: улучшений {improvements}…");
            }

            ct.ThrowIfCancellationRequested();
            var result = new SolveResult
            {
                AlgorithmName = DisplayName,
                Score = engine.Score,
                Iterations = improvements,
                Elapsed = sw.Elapsed
            };
            result.Assignments.AddRange(engine.AllAssignments);
            result.Unplaced = problem.Demands.Where(d => engine.GetAssignment(d.Id) == null).ToList();
            progress?.Report(new SolveProgress { Percent = 100, Message = "Локальный поиск завершён", Score = engine.Score });
            return result;
        }
    }

    /// <summary>Имитация отжига: случайные переносы с принятием худших решений по вероятности.</summary>
    public sealed class SimulatedAnnealingAlgorithm : ISchedulingAlgorithm
    {
        public string Id => "annealing";
        public string DisplayName => "Имитация отжига";
        public string Description => "Ищет глобальный optimum: переносит занятия, в том числе в худшие ячейки, с постепенным затуханием «температуры». Дольше жадного, но качество выше.";
        public ComplexityLevel TypicalLevel => ComplexityLevel.Optimal;

        public SolveResult Solve(ScheduleProblem problem, SchedulingParams parameters,
            IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var rnd = new Random(parameters.RandomSeed);
            var engine = LocalSearchBase.BuildGreedy(problem, rnd, progress, ct);
            var demands = problem.Demands.Where(d => engine.GetAssignment(d.Id) != null).ToList();
            if (demands.Count == 0) return LocalSearchBase.EmptyResult(DisplayName, sw.Elapsed);

            var bestEngine = LocalSearchBase.Clone(problem, engine);
            var temperature = 80.0;
            var cooling = 0.9995;
            var iter = 0;

            while (sw.ElapsedMilliseconds < parameters.TimeBudgetMs && temperature > 0.05)
            {
                ct.ThrowIfCancellationRequested();
                iter++;
                var demand = demands[rnd.Next(demands.Count)];
                var current = engine.GetAssignment(demand.Id);
                if (current == null) continue;
                var currentScore = engine.ScorePlacement(demand, current.Day, current.SlotId, current.RoomId);

                engine.Unplace(demand.Id);
                var cands = AlgorithmSupport.BuildCandidates(engine, problem, demand, 40);
                if (cands.Count == 0)
                {
                    engine.ForcePlace(current);
                    temperature *= cooling;
                    continue;
                }

                var pick = cands[rnd.Next(cands.Count)];
                var delta = pick.Score - currentScore;
                if (delta >= 0 || rnd.NextDouble() < Math.Exp(delta / temperature))
                {
                    engine.ForcePlace(new Assignment { Demand = demand, Day = pick.Day, SlotId = pick.SlotId, RoomId = pick.RoomId });
                }
                else
                {
                    engine.ForcePlace(current);
                }
                temperature *= cooling;

                if (engine.Score > bestEngine.Score)
                {
                    LocalSearchBase.CopyInto(problem, engine, bestEngine);
                }

                if (iter % 500 == 0)
                    AlgorithmSupport.Report(progress, sw, 100, (int)(sw.ElapsedMilliseconds * 100.0 / parameters.TimeBudgetMs),
                        bestEngine.Score, $"Отжиг: T={temperature:F2}, лучший счёт {bestEngine.Score:F0}…");
            }

            ct.ThrowIfCancellationRequested();
            var result = new SolveResult
            {
                AlgorithmName = DisplayName,
                Score = bestEngine.Score,
                Iterations = iter,
                Elapsed = sw.Elapsed
            };
            result.Assignments.AddRange(bestEngine.AllAssignments);
            result.Unplaced = problem.Demands.Where(d => bestEngine.GetAssignment(d.Id) == null).ToList();
            progress?.Report(new SolveProgress { Percent = 100, Message = "Отжиг завершён", Score = bestEngine.Score });
            return result;
        }
    }

    /// <summary>Генетический алгоритм: популяция «хромосом» (выбор ячейки на каждое занятие), отбор, скрещивание, мутации.</summary>
    public sealed class GeneticAlgorithm : ISchedulingAlgorithm
    {
        public string Id => "genetic";
        public string DisplayName => "Генетический";
        public string Description => "Эволюционный поиск: популяция расписаний, скрещивание и мутации с сохранением лучшего решения. Самый продолжительный режим, максимальное качество.";
        public ComplexityLevel TypicalLevel => ComplexityLevel.Optimal;

        private sealed class Chromosome
        {
            public int[] Genes;   // индекс в списке кандидатов для каждого demand
            public double Fitness;
            public bool[] HardOk; // совместимость генов между собой проверяется при оценке
        }

        public SolveResult Solve(ScheduleProblem problem, SchedulingParams parameters,
            IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var rnd = new Random(parameters.RandomSeed);
            var demandCount = problem.Demands.Length;
            var popSize = demandCount > 800 ? 24 : demandCount > 300 ? 36 : 50;
            var mutationRate = 0.08;
            const double HardPenalty = 100000.0;

            // Кандидаты считаются один раз по «эталонному» жадному состоянию; жёсткие конфликты между генами штрафуются
            var engine = LocalSearchBase.BuildGreedy(problem, rnd, progress, ct);
            var candidateLists = new List<List<Candidate>>(demandCount);
            foreach (var d in problem.Demands)
                candidateLists.Add(AlgorithmSupport.BuildCandidates(engine, problem, d, 14));

            double Evaluate(Chromosome ch, List<Assignment> assignmentsOut)
            {
                var busyTeacher = new HashSet<long>();
                var busyRoom = new HashSet<long>();
                var busyGroup = new HashSet<long>();
                var score = 0.0;
                var hard = 0;
                assignmentsOut?.Clear();

                for (var i = 0; i < demandCount; i++)
                {
                    var list = candidateLists[i];
                    if (list.Count == 0) continue;
                    var g = ch.Genes[i] % list.Count;
                    var c = list[g];
                    var demand = problem.Demands[i];

                    if (!busyTeacher.Add(TeacherKey(demand.TeacherId, c))) hard++;
                    if (!busyRoom.Add(RoomKey(c))) hard++;
                    foreach (var gid in demand.GroupIds)
                        if (!busyGroup.Add(GroupKey(gid, c))) hard++;

                    score += engine.ScorePlacement(demand, c.Day, c.SlotId, c.RoomId);
                    assignmentsOut?.Add(new Assignment { Demand = demand, Day = c.Day, SlotId = c.SlotId, RoomId = c.RoomId });
                }
                return score - hard * HardPenalty;

                static long TeacherKey(int teacherId, Candidate c) => (1L << 61) ^ ((long)teacherId << 40) ^ ((long)c.Day << 32) ^ (uint)c.SlotId;
                static long RoomKey(Candidate c) => (1L << 62) ^ ((long)c.RoomId << 40) ^ ((long)c.Day << 32) ^ (uint)c.SlotId;
                static long GroupKey(int gid, Candidate c) => ((long)gid << 40) ^ ((long)c.Day << 32) ^ (uint)c.SlotId;
            }

            var population = new List<Chromosome>(popSize);
            var seed = new Chromosome { Genes = new int[demandCount] };
            for (var i = 0; i < demandCount; i++) seed.Genes[i] = 0;
            seed.Fitness = Evaluate(seed, null);
            population.Add(seed);

            for (var i = 1; i < popSize; i++)
            {
                ct.ThrowIfCancellationRequested();
                var ch = new Chromosome { Genes = new int[demandCount] };
                for (var j = 0; j < demandCount; j++)
                {
                    var list = candidateLists[j];
                    ch.Genes[j] = list.Count == 0 ? 0 : rnd.Next(list.Count);
                }
                ch.Fitness = Evaluate(ch, null);
                population.Add(ch);
            }

            Chromosome best = population.OrderByDescending(c => c.Fitness).First();
            var generation = 0;

            while (sw.ElapsedMilliseconds < parameters.TimeBudgetMs)
            {
                ct.ThrowIfCancellationRequested();
                generation++;
                population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
                if (population[0].Fitness > best.Fitness) best = Clone(population[0]);

                var next = new List<Chromosome> { Clone(population[0]), Clone(population[1]) };
                while (next.Count < popSize)
                {
                    var p1 = Tournament(population, rnd);
                    var p2 = Tournament(population, rnd);
                    var child = new Chromosome { Genes = new int[demandCount] };
                    for (var j = 0; j < demandCount; j++)
                        child.Genes[j] = rnd.NextDouble() < 0.5 ? p1.Genes[j] : p2.Genes[j];
                    for (var j = 0; j < demandCount; j++)
                    {
                        if (rnd.NextDouble() < mutationRate && candidateLists[j].Count > 0)
                            child.Genes[j] = rnd.Next(candidateLists[j].Count);
                    }
                    child.Fitness = Evaluate(child, null);
                    next.Add(child);
                }
                population = next;

                if (generation % 5 == 0)
                    AlgorithmSupport.Report(progress, sw, 100, (int)(sw.ElapsedMilliseconds * 100.0 / parameters.TimeBudgetMs),
                        best.Fitness, $"Генетический поиск: поколение {generation}, лучший счёт {best.Fitness:F0}…");
            }

            ct.ThrowIfCancellationRequested();
            var finalAssignments = new List<Assignment>();
            best.Fitness = Evaluate(best, finalAssignments);

            // Полировка лучшей особи. Переразмещение выполняется с проверкой жёстких ограничений,
            // чтобы итоговое расписание гарантированно не содержало нарушений.
            var polishEngine = new ConstraintEngine(problem);
            var dropped = new List<Demand>();
            foreach (var a in finalAssignments)
            {
                if (polishEngine.CheckHard(a.Demand, a.Day, a.SlotId, a.RoomId).Count == 0)
                    polishEngine.ForcePlace(a);
                else
                    dropped.Add(a.Demand);
            }
            // Пытаемся пристроить «выпавшие» из-за конфликтов занятия в любую допустимую ячейку.
            foreach (var d in dropped)
            {
                var cands = AlgorithmSupport.BuildCandidates(polishEngine, problem, d, 1);
                if (cands.Count > 0)
                    polishEngine.ForcePlace(new Assignment { Demand = d, Day = cands[0].Day, SlotId = cands[0].SlotId, RoomId = cands[0].RoomId });
            }

            LocalSearchBase.Improve(problem, polishEngine, rnd, sw, parameters.TimeBudgetMs, progress, ct);

            var result = new SolveResult
            {
                AlgorithmName = DisplayName,
                Score = polishEngine.Score,
                Iterations = generation,
                Elapsed = sw.Elapsed
            };
            result.Assignments.AddRange(polishEngine.AllAssignments);
            result.Unplaced = problem.Demands.Where(d => polishEngine.GetAssignment(d.Id) == null).ToList();
            progress?.Report(new SolveProgress { Percent = 100, Message = "Генетический поиск завершён", Score = result.Score });
            return result;

            Chromosome Tournament(List<Chromosome> pop, Random r)
            {
                var a = pop[r.Next(pop.Count)];
                var b = pop[r.Next(pop.Count)];
                return a.Fitness >= b.Fitness ? a : b;
            }

            Chromosome Clone(Chromosome c) => new Chromosome { Genes = (int[])c.Genes.Clone(), Fitness = c.Fitness };
        }
    }

    internal static class LocalSearchBase
    {
        public static ConstraintEngine BuildGreedy(ScheduleProblem problem, Random rnd,
            IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var engine = new ConstraintEngine(problem);
            var ordered = AlgorithmSupport.OrderDemands(problem, rnd);
            var done = 0;
            foreach (var demand in ordered)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                if (engine.GetAssignment(demand.Id) != null) continue;
                var cands = AlgorithmSupport.BuildCandidates(engine, problem, demand, 6);
                if (cands.Count > 0)
                {
                    var c = cands[0];
                    engine.ForcePlace(new Assignment { Demand = demand, Day = c.Day, SlotId = c.SlotId, RoomId = c.RoomId });
                }
                if (done % 50 == 0)
                    AlgorithmSupport.Report(progress, null, ordered.Count, done, engine.Score, "Жадная инициализация…");
            }
            return engine;
        }

        public static ConstraintEngine FromAssignments(ScheduleProblem problem, List<Assignment> assignments)
        {
            var engine = new ConstraintEngine(problem);
            foreach (var a in assignments) engine.ForcePlace(a);
            return engine;
        }

        public static void Improve(ScheduleProblem problem, ConstraintEngine engine, Random rnd,
            Stopwatch sw, int budgetMs, IProgress<SolveProgress> progress, CancellationToken ct)
        {
            var demands = problem.Demands.Where(d => engine.GetAssignment(d.Id) != null).ToList();
            var idle = 0;
            while (sw.ElapsedMilliseconds < budgetMs && idle < 2)
            {
                ct.ThrowIfCancellationRequested();
                var improved = false;
                foreach (var demand in demands.Shuffled(rnd))
                {
                    var current = engine.GetAssignment(demand.Id);
                    if (current == null) continue;
                    var currentScore = engine.ScorePlacement(demand, current.Day, current.SlotId, current.RoomId);
                    engine.Unplace(demand.Id);
                    Candidate? best = null;
                    var bestGain = 1e-6;
                    foreach (var c in AlgorithmSupport.BuildCandidates(engine, problem, demand, 20))
                    {
                        var gain = c.Score - currentScore;
                        if (gain > bestGain) { bestGain = gain; best = c; }
                    }
                    if (best.HasValue)
                    {
                        var b = best.Value;
                        engine.ForcePlace(new Assignment { Demand = demand, Day = b.Day, SlotId = b.SlotId, RoomId = b.RoomId });
                        improved = true;
                    }
                    else engine.ForcePlace(current);
                }
                idle = improved ? 0 : idle + 1;
            }
        }

        public static SolveResult EmptyResult(string name, TimeSpan elapsed) =>
            new SolveResult { AlgorithmName = name, Score = 0, Elapsed = elapsed };

        public static ConstraintEngine Clone(ScheduleProblem problem, ConstraintEngine source)
        {
            var engine = new ConstraintEngine(problem);
            foreach (var a in source.AllAssignments) engine.ForcePlace(a);
            return engine;
        }

        public static void CopyInto(ScheduleProblem problem, ConstraintEngine source, ConstraintEngine target)
        {
            var list = source.AllAssignments.ToList();
            foreach (var a in target.AllAssignments.ToList()) target.Unplace(a.Demand.Id);
            foreach (var a in list) target.ForcePlace(a);
        }

        public static List<T> Shuffled<T>(this IEnumerable<T> source, Random rnd)
        {
            var list = source.ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rnd.Next(i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
            return list;
        }
    }
}
