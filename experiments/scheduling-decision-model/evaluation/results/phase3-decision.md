## Phase 3 decision (3600 holdout instances, budget 1826301)

3. Hard gates (model): invalid accepted 0; false infeasible 0; fallbacks 0. Baseline unsafe outcomes: 0.
4. Primary, test-unseen-family (n=2400): (a) solved diff -4.042 pts [-5.125, -3.000] -> fail (needs low >= +10); (b) work ratio 0.950x [0.874, 1.037] -> fail (needs high <= 0.5).
5. unseen family multi-skill (n=1200): solved diff -8.000 pts [-10.083, -6.083] REGRESSION; work ratio 1.302x [1.109, 1.550]
5. unseen family shift-gaps (n=1200): solved diff -0.083 pts [-0.667, 0.500]; work ratio 0.754x [0.708, 0.810]
5. unseen size (all) (n=600): solved diff -9.500 pts [-12.667, -6.333] REGRESSION; work ratio 1.213x [0.942, 1.592]
5. unseen size jobshop-chains (n=200): solved diff -8.000 pts [-13.000, -3.000] REGRESSION; work ratio 0.769x [0.574, 1.117]
5. unseen size parallel-contention (n=200): solved diff -9.000 pts [-14.000, -4.500] REGRESSION; work ratio 1.161x [0.841, 1.666]
5. unseen size project-dag (n=200): solved diff -11.500 pts [-18.500, -5.000] REGRESSION; work ratio 2.682x [1.321, 5.898]
6. Hard cases for dom-dynamic in test-unseen-family: 362 (needs >= 300) -> met
   secondary, test-seen (n=600): solved diff -1.000 pts [-2.000, 0.000]; work ratio 0.853x [0.795, 0.917]
   secondary, hard subset of test-unseen-family (n=362): solved diff -13.536 pts [-18.579, -8.152]; work ratio 0.546x [0.293, 1.024]
   CP-SAT vs dom-dynamic, test-unseen-family: solved diff 4.958 pts [3.958, 6.000]; CP-SAT solved 99.9% vs dom-dynamic 95.0% vs model 90.9%

7. Decision: NO GO (regression: unseen family multi-skill, unseen size (all), unseen size jobshop-chains, unseen size parallel-contention, unseen size project-dag)
