# Difficulty calibration (fixed enemy-HP multiplier, no jitter)

Seeds 1..10 per cell, Unity 6000.6.8f1, wall 127.984 s.

| HP x | Policy | Episodes | Win rate | Mean lives | Mean R |
|---|---|---|---|---|---|
| 1 | Random | 10 | 0.8 | 7 | 184 |
| 1 | Heuristic | 10 | 1 | 9 | 230 |
| 1.1 | Random | 10 | 0.8 | 6.4 | 178 |
| 1.1 | Heuristic | 10 | 1 | 9 | 230 |
| 1.2 | Random | 10 | 0.7 | 4.5 | 146.5 |
| 1.2 | Heuristic | 10 | 1 | 3 | 170 |
| 1.3 | Random | 10 | 0.5 | 3.3 | 109.5 |
| 1.3 | Heuristic | 10 | 1 | 2 | 160 |
| 1.4 | Random | 10 | 0.2 | 1.2 | 50.5 |
| 1.4 | Heuristic | 10 | 0 | 0 | 10 |
| 1.5 | Random | 10 | 0.2 | 1.4 | 52.5 |
| 1.5 | Heuristic | 10 | 0 | 0 | 10 |
| 2 | Random | 10 | 0 | 0 | 10 |
| 2 | Heuristic | 10 | 0 | 0 | 10 |
| 2.5 | Random | 10 | 0 | 0 | 10 |
| 2.5 | Heuristic | 10 | 0 | 0 | 10 |
| 3 | Random | 10 | 0 | 0 | 10 |
| 3 | Heuristic | 10 | 0 | 0 | 10 |
| 4 | Random | 10 | 0 | 0 | 10 |
| 4 | Heuristic | 10 | 0 | 0 | 10 |

## Chosen training band: HP x 1.3 - 1.4

The originally suggested grid (1.0, 1.5, 2.0, 2.5, 3.0, 4.0) has no useful band: the game has a sharp difficulty
cliff between x1.3 and x1.4. From x2.0 up both policies lose every episode (R = 10, 0 lives); at x1.0-1.1 the
heuristic wins 100%. The grid was therefore refined with 1.1-1.4.

No band separates the policies cleanly. Heuristic win rate is 100% up to x1.3 and 0% from x1.4, so a 20-80% band
only exists as a mix of the two sides of the cliff. Random is lower than the heuristic at x1.2-1.3 (0.7/0.5 vs 1.0)
but HIGHER at x1.4-1.5 (0.2 vs 0.0; the heuristic's spending pattern is worse than random there).
Chosen band: [1.3, 1.4] with per-episode uniform jitter, where the heuristic win rate is expected near 50%
(the cell where it flips) and Random is 0.5 -> 0.2. Reason: the only region where the outcome is not already decided,
so a learned policy has room to beat both baselines. Caveat: 10 seeds per cell, win rates have wide CIs, and the
heuristic's win rate inside the band has not been measured directly (only at the endpoints).
