budget 1826301 work units; 600 instances; split(s) validation

| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| incumbent | 94.0 | 95.1 | 89.8 | 0 | 0 | 0 | 0.9 | 68.5 |
| incumbent+dedupe | 94.0 | 95.1 | 89.8 | 0 | 0 | 0 | 1.3 | 66.1 |
| dom-dynamic | 98.8 | 100.0 | 94.5 | 0 | 0 | 0 | 1.6 | 4.7 |
| least-slack | 94.3 | 99.8 | 74.2 | 0 | 0 | 0 | 2.1 | 32.9 |
| dom-dynamic+lcv | 98.7 | 99.8 | 94.5 | 0 | 0 | 0 | 3.2 | 16.7 |
| greedy-then-search | 94.7 | 96.0 | 89.8 | 0 | 0 | 0 | 1.1 | 42.8 |
| cpsat | 100.0 | 100.0 | 100.0 | 0 | 0 | 0 | 4.4 | 8.1 |

Paired against incumbent (group bootstrap, 95% CI):
| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |
| --- | --- | --- |
| incumbent+dedupe | +0.0 [+0.0, +0.0] | 1.000 [1.000, 1.000] (n=449) |
| dom-dynamic | +4.8 [+2.7, +7.3] | 10.111 [7.220, 13.636] (n=472) |
| least-slack | +0.3 [-2.2, +3.0] | 12.692 [9.074, 17.233] (n=471) |
| dom-dynamic+lcv | +4.7 [+2.5, +7.2] | 61.390 [43.220, 85.190] (n=471) |
| greedy-then-search | +0.7 [+0.0, +1.7] | 0.909 [0.760, 1.048] (n=453) |
| cpsat | +6.0 [+3.8, +8.5] | n/a (wall-limited) |

Solved % by family and size:
| cell | n | incumbent | incumbent+dedupe | dom-dynamic | least-slack | dom-dynamic+lcv | greedy-then-search | cpsat |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| jobshop-chains/medium | 100 | 94.0 | 94.0 | 99.0 | 96.0 | 99.0 | 98.0 | 100.0 |
| jobshop-chains/small | 100 | 98.0 | 98.0 | 98.0 | 98.0 | 98.0 | 98.0 | 100.0 |
| parallel-contention/medium | 100 | 91.0 | 91.0 | 96.0 | 81.0 | 96.0 | 91.0 | 100.0 |
| parallel-contention/small | 100 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 |
| project-dag/medium | 100 | 81.0 | 81.0 | 100.0 | 91.0 | 99.0 | 81.0 | 100.0 |
| project-dag/small | 100 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 |

Instances where every engine method needs > 1% of budget or fails: 23
