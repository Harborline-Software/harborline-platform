budget 18263015 work units; 2400 instances; split(s) test-unseen-family

| method | solved % | feasible found % | infeasible proved % | invalid accepted | false infeasible | fallbacks | wall p50 ms | wall p90 ms |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| incumbent | 76.8 | 76.8 | 77.0 | 0 | 0 | 0 | 7.2 | 8039.1 |
| incumbent+dedupe | 77.2 | 77.1 | 77.6 | 0 | 0 | 0 | 7.4 | 8190.2 |
| dom-dynamic | 97.2 | 99.3 | 88.5 | 0 | 0 | 0 | 2.1 | 42.4 |
| least-slack | 92.4 | 98.1 | 68.0 | 0 | 0 | 0 | 2.5 | 1114.1 |
| dom-dynamic+lcv | 96.9 | 99.4 | 86.3 | 0 | 0 | 0 | 6.3 | 172.2 |
| greedy-then-search | 83.7 | 85.3 | 77.0 | 0 | 0 | 0 | 1.4 | 6764.2 |
| cpsat | 100.0 | 100.0 | 99.8 | 0 | 0 | 0 | 7.5 | 19.3 |
| learned | 94.4 | 97.1 | 83.3 | 0 | 0 | 0 | 1.8 | 416.8 |

Paired against incumbent (group bootstrap, 95% CI):
| method | solved diff pts [CI] | work ratio, geomean [CI] (engine methods) |
| --- | --- | --- |
| incumbent+dedupe | +0.3 [+0.1, +0.7] | 0.942 [0.905, 0.975] (n=1495) |
| dom-dynamic | +20.4 [+18.3, +22.5] | 0.151 [0.113, 0.199] (n=1926) |
| least-slack | +15.5 [+13.4, +17.9] | 0.181 [0.137, 0.241] (n=1917) |
| dom-dynamic+lcv | +20.1 [+18.1, +22.2] | 1.468 [1.095, 1.954] (n=1930) |
| greedy-then-search | +6.8 [+5.6, +8.1] | 0.034 [0.025, 0.045] (n=1654) |
| cpsat | +23.1 [+21.1, +25.4] | n/a (wall-limited) |
| learned | +17.6 [+15.7, +19.6] | 0.147 [0.112, 0.192] (n=1890) |

Solved % by family and size:
| cell | n | incumbent | incumbent+dedupe | dom-dynamic | least-slack | dom-dynamic+lcv | greedy-then-search | cpsat | learned |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| multi-skill/medium | 800 | 55.2 | 56.1 | 92.2 | 81.6 | 91.5 | 55.5 | 99.9 | 84.4 |
| multi-skill/small | 400 | 95.2 | 95.5 | 99.5 | 99.2 | 99.5 | 95.2 | 100.0 | 99.5 |
| shift-gaps/medium | 800 | 79.2 | 79.2 | 99.6 | 95.9 | 99.5 | 97.9 | 100.0 | 99.1 |
| shift-gaps/small | 400 | 96.8 | 96.8 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 |

Instances where every engine method needs > 1% of budget or fails: 136
