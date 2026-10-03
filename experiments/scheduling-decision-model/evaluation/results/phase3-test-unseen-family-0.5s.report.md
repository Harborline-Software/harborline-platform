budget 1826301 work units; 2400 instances; split(s) test-unseen-family

| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| incumbent | 70.8 | 70.8 | 70.4 | 0 | 0 | 0 | 8.2 | 921.4 |
| incumbent+dedupe | 71.0 | 71.1 | 70.7 | 0 | 0 | 0 | 8.4 | 933.0 |
| dom-dynamic | 95.0 | 98.0 | 82.0 | 0 | 0 | 0 | 2.4 | 44.3 |
| least-slack | 89.6 | 97.5 | 56.3 | 0 | 0 | 0 | 2.8 | 660.8 |
| dom-dynamic+lcv | 94.5 | 98.6 | 77.6 | 0 | 0 | 0 | 7.3 | 189.5 |
| greedy-then-search | 80.5 | 82.9 | 70.4 | 0 | 0 | 0 | 1.7 | 762.3 |
| cpsat | 99.9 | 100.0 | 99.6 | 0 | 0 | 0 | 7.7 | 19.6 |
| learned | 90.9 | 94.9 | 74.1 | 0 | 0 | 0 | 2.1 | 419.6 |

Paired against incumbent (group bootstrap, 95% CI):
| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |
| --- | --- | --- |
| incumbent+dedupe | +0.3 [+0.0, +0.6] | 0.950 [0.914, 0.980] (n=1381) |
| dom-dynamic | +24.2 [+22.1, +26.4] | 0.263 [0.209, 0.337] (n=1903) |
| least-slack | +18.8 [+16.4, +21.3] | 0.314 [0.246, 0.398] (n=1907) |
| dom-dynamic+lcv | +23.8 [+21.7, +26.0] | 2.642 [2.052, 3.406] (n=1919) |
| greedy-then-search | +9.8 [+8.3, +11.3] | 0.041 [0.031, 0.053] (n=1609) |
| cpsat | +29.2 [+26.9, +31.4] | n/a (wall-limited) |
| learned | +20.2 [+18.0, +22.5] | 0.241 [0.190, 0.306] (n=1855) |

Solved % by family and size:
| cell | n | incumbent | incumbent+dedupe | dom-dynamic | least-slack | dom-dynamic+lcv | greedy-then-search | cpsat | learned |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| multi-skill/medium | 800 | 49.0 | 49.5 | 86.6 | 76.5 | 85.4 | 49.4 | 99.8 | 75.1 |
| multi-skill/small | 400 | 91.2 | 92.0 | 99.0 | 97.0 | 98.8 | 91.2 | 100.0 | 98.0 |
| shift-gaps/medium | 800 | 70.2 | 70.2 | 98.8 | 93.8 | 98.9 | 96.6 | 100.0 | 98.6 |
| shift-gaps/small | 400 | 94.8 | 94.8 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 |

Instances where every engine method needs > 1% of budget or fails: 247
