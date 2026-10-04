budget 1826301 work units; 600 instances; split(s) test-seen

| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| incumbent | 94.7 | 96.6 | 89.6 | 0 | 0 | 0 | 0.7 | 86.8 |
| incumbent+dedupe | 94.7 | 96.6 | 89.6 | 0 | 0 | 0 | 1.1 | 91.1 |
| dom-dynamic | 98.3 | 99.8 | 94.5 | 0 | 0 | 0 | 1.2 | 4.9 |
| least-slack | 93.3 | 99.5 | 76.7 | 0 | 0 | 0 | 1.7 | 124.9 |
| dom-dynamic+lcv | 98.5 | 100.0 | 94.5 | 0 | 0 | 0 | 2.1 | 18.6 |
| greedy-then-search | 95.0 | 97.0 | 89.6 | 0 | 0 | 0 | 0.7 | 57.5 |
| cpsat | 100.0 | 100.0 | 100.0 | 0 | 0 | 0 | 4.2 | 8.5 |
| learned | 97.3 | 100.0 | 90.2 | 0 | 0 | 0 | 0.8 | 5.9 |

Paired against incumbent (group bootstrap, 95% CI):
| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |
| --- | --- | --- |
| incumbent+dedupe | +0.0 [+0.0, +0.0] | 1.000 [1.000, 1.000] (n=422) |
| dom-dynamic | +3.7 [+1.7, +6.0] | 9.820 [7.052, 13.384] (n=436) |
| least-slack | -1.3 [-4.0, +1.3] | 12.674 [9.007, 17.390] (n=436) |
| dom-dynamic+lcv | +3.8 [+1.8, +6.2] | 61.165 [42.640, 86.509] (n=437) |
| greedy-then-search | +0.3 [+0.0, +0.8] | 0.859 [0.716, 1.002] (n=424) |
| cpsat | +5.3 [+3.2, +7.7] | n/a (wall-limited) |
| learned | +2.7 [+0.7, +4.8] | 8.338 [6.034, 11.192] (n=437) |

Solved % by family and size:
| cell | n | incumbent | incumbent+dedupe | dom-dynamic | least-slack | dom-dynamic+lcv | greedy-then-search | cpsat | learned |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| jobshop-chains/medium | 100 | 97.0 | 97.0 | 100.0 | 98.0 | 100.0 | 99.0 | 100.0 | 100.0 |
| jobshop-chains/small | 100 | 100.0 | 100.0 | 98.0 | 97.0 | 98.0 | 100.0 | 100.0 | 97.0 |
| parallel-contention/medium | 100 | 89.0 | 89.0 | 98.0 | 79.0 | 98.0 | 89.0 | 100.0 | 93.0 |
| parallel-contention/small | 100 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 |
| project-dag/medium | 100 | 85.0 | 85.0 | 96.0 | 87.0 | 97.0 | 85.0 | 100.0 | 96.0 |
| project-dag/small | 100 | 97.0 | 97.0 | 98.0 | 99.0 | 98.0 | 97.0 | 100.0 | 98.0 |

Instances where every engine method needs > 1% of budget or fails: 19
