## Phase 3 decision (2400 holdout instances, budget 18263015)

3. Hard gates (model): invalid accepted 0; false infeasible 0; fallbacks 0. Baseline unsafe outcomes: 0.
4. Primary, test-unseen-family (n=2400): (a) solved diff -2.792 pts [-3.667, -2.000] -> fail (needs low >= +10); (b) work ratio 1.013x [0.918, 1.122] -> fail (needs high <= 0.5).
5. unseen family multi-skill (n=1200): solved diff -5.250 pts [-7.083, -3.667] REGRESSION; work ratio 1.493x [1.219, 1.857]
5. unseen family shift-gaps (n=1200): solved diff -0.333 pts [-0.667, 0.000]; work ratio 0.758x [0.711, 0.817]
6. Hard cases for dom-dynamic in test-unseen-family: 217 (needs >= 300) -> NOT MET
   secondary, hard subset of test-unseen-family (n=217): solved diff -13.364 pts [-19.718, -6.667]; work ratio 0.360x [0.126, 0.996]
   CP-SAT vs dom-dynamic, test-unseen-family: solved diff 2.750 pts [2.042, 3.542]; CP-SAT solved 100.0% vs dom-dynamic 97.2% vs model 94.4%

7. Decision: INCONCLUSIVE (hard-case floor not met) -> NO GO for adoption
