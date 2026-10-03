## Phase 3 decision (2400 holdout instances, budget 18263015)

3. Hard gates (model): invalid accepted 0; false infeasible 0; fallbacks 0. Baseline unsafe outcomes: 0.
4. Primary, test-unseen-family (n=2400): (a) solved diff -2.792 pts [-3.667, -2.000] -> fail (needs low >= +10); (b) work ratio 1.013x [0.918, 1.122] -> fail (needs high <= 0.5).
5. unseen family multi-skill (n=1200): solved diff -5.250 pts [-7.083, -3.667] REGRESSION; work ratio 1.493x [1.219, 1.857]
5. unseen family shift-gaps (n=1200): solved diff -0.333 pts [-0.667, 0.000]; work ratio 0.758x [0.711, 0.817]
Unhandled exception. System.InvalidOperationException: Sequence contains no elements
   at System.Linq.ThrowHelper.ThrowNoElementsException()
   at System.Linq.Enumerable.Average[TSource,TSelector,TAccumulator,TResult](IEnumerable`1 source, Func`2 selector)
   at System.Linq.Enumerable.Average[TSource](IEnumerable`1 source, Func`2 selector)
   at Harborline.Experiments.SchedulingDecisionModel.Evaluation.Stats.<>c.<SolvedDifference>b__2_0(IReadOnlyList`1 sample) in /Users/christopherwood/Projects/Harborline/harborline-platform/experiments/scheduling-decision-model/evaluation/Stats.cs:line 15
   at Harborline.Experiments.SchedulingDecisionModel.Evaluation.Stats.Bootstrap[T](IReadOnlyList`1 rows, UInt64 seed, Func`2 statistic) in /Users/christopherwood/Projects/Harborline/harborline-platform/experiments/scheduling-decision-model/evaluation/Stats.cs:line 30
   at Harborline.Experiments.SchedulingDecisionModel.Evaluation.Stats.SolvedDifference(IReadOnlyList`1 rows, UInt64 seed) in /Users/christopherwood/Projects/Harborline/harborline-platform/experiments/scheduling-decision-model/evaluation/Stats.cs:line 15
   at Program.<>c__DisplayClass0_16.<<Main>$>g__Solved|97(IEnumerable`1 ids) in /Users/christopherwood/Projects/Harborline/harborline-platform/experiments/scheduling-decision-model/evaluation/Program.cs:line 440
   at Program.<>c__DisplayClass0_0.<<Main>$>g__Decide|15(Int64 budget, String[] files) in /Users/christopherwood/Projects/Harborline/harborline-platform/experiments/scheduling-decision-model/evaluation/Program.cs:line 470
   at Program.<Main>$(String[] args) in /Users/christopherwood/Projects/Harborline/harborline-platform/experiments/scheduling-decision-model/evaluation/Program.cs:line 36
