budget 1826301 work units; 600 instances; split(s) test-unseen-size

| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| incumbent | 59.7 | 67.7 | 51.0 | 0 | 0 | 0 | 52.0 | 1273.3 |
| incumbent+dedupe | 59.7 | 67.7 | 51.0 | 0 | 0 | 0 | 54.4 | 1248.6 |
| dom-dynamic | 80.8 | 97.4 | 63.1 | 0 | 0 | 0 | 7.2 | 1215.6 |
| least-slack | 58.8 | 94.8 | 20.3 | 0 | 0 | 0 | 18.8 | 1521.3 |
| dom-dynamic+lcv | 80.2 | 98.7 | 60.3 | 0 | 0 | 0 | 21.8 | 990.1 |
| greedy-then-search | 63.0 | 74.2 | 51.0 | 0 | 0 | 0 | 15.0 | 1134.0 |
| cpsat | 99.7 | 100.0 | 99.3 | 0 | 0 | 0 | 6.7 | 15.9 |
| learned | 71.3 | 88.7 | 52.8 | 0 | 0 | 0 | 5.9 | 1029.2 |

Paired against incumbent (group bootstrap, 95% CI):
| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |
| --- | --- | --- |
| incumbent+dedupe | +0.0 [+0.0, +0.0] | 1.000 [1.000, 1.000] (n=210) |
| dom-dynamic | +21.2 [+17.0, +25.3] | 0.945 [0.528, 1.664] (n=302) |
| least-slack | -0.8 [-6.2, +4.8] | 1.380 [0.726, 2.509] (n=297) |
| dom-dynamic+lcv | +20.5 [+16.2, +24.7] | 6.305 [3.434, 11.613] (n=306) |
| greedy-then-search | +3.3 [+1.5, +5.3] | 0.364 [0.207, 0.612] (n=230) |
| cpsat | +40.0 [+35.2, +44.7] | n/a (wall-limited) |
| learned | +11.7 [+7.7, +15.7] | 1.163 [0.659, 2.070] (n=278) |

Solved % by family and size:
| cell | n | incumbent | incumbent+dedupe | dom-dynamic | least-slack | dom-dynamic+lcv | greedy-then-search | cpsat | learned |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| jobshop-chains/large | 200 | 62.0 | 62.0 | 76.5 | 65.5 | 75.5 | 71.5 | 100.0 | 68.5 |
| parallel-contention/large | 200 | 69.5 | 69.5 | 91.0 | 60.5 | 90.0 | 70.0 | 99.0 | 82.0 |
| project-dag/large | 200 | 47.5 | 47.5 | 75.0 | 50.5 | 75.0 | 47.5 | 100.0 | 63.5 |

Instances where every engine method needs > 1% of budget or fails: 168
