# Tower Defence

A 3D tower defence game made in Unity. Place turrets on build nodes, earn money for kills, and stop eight waves of enemies before they drain your 10 lives. The project also contains a small learned "builder" AI that can play the game for you and doubles as a balance-testing tool.

## Demo

TODO (not done yet): record a short gameplay GIF and add it here.

## Features

- 8 hand-built waves of three enemy types (Grunt, Fast, Tank). Win by clearing all of them, lose when lives reach 0.
- Three turret types: Standard, Missile (area damage), Laser (continuous damage and slow). Costs 100 / 250 / 300, start money 500.
- Build nodes along a winding path, shop bar, money / lives / wave HUD, pause screen, game over and win screens.
- Camera pan and zoom.
- The whole scene, prefabs, materials and waves are generated from code (no external art).
- In-game AI auto-play (button or `B`) and a headless, fast-forward evaluation harness.

## Run from source

1. Install Unity **6000.6.8f1** (see `ProjectSettings/ProjectVersion.txt`).
2. Clone the repo and open the repo folder in Unity Hub.
3. Open `Assets/Scenes/Main.unity` and press Play.

Controls:

| Input | Action |
|---|---|
| Mouse | Pick a turret in the shop bar, click a build node to place it |
| `P` | Pause / resume |
| `W` `A` `S` `D` (or arrows) | Move the camera |
| Mouse wheel | Zoom the camera |
| `B` (or the "AI: OFF" button, bottom left) | Toggle AI auto-play |

## How the scene and assets are generated

`Assets/Editor/SceneBuilder.cs` creates everything under `Assets/Generated` and the scene `Assets/Scenes/Main.unity`. Use the menu **TowerDefence > Build Scene**, or from the command line:

```
Unity -batchmode -nographics -projectPath <repo> -executeMethod SceneBuilder.Build -quit
```

The log contains a line starting with `BUILD OK` on success.

## AI builder agent

The AI decides which turret type (none / Standard / Missile / Laser) to place on which of the 24 build slots closest to the path, once per second of game time.

- **Policy:** a small MLP (observation -> 24 tanh units -> 73 action logits, about 1.4k parameters) with invalid actions masked out. Weights are in `Assets/Resources/builder_policy.json` together with training metadata.
- **Training:** OpenAI-style Evolution Strategies (antithetic sampling, centered ranks, seeded), implemented in C# and run inside Unity in batch mode. Return = 100 * win + 10 * lives left + 5 * waves cleared.
- **Why not ML-Agents:** `pip` on the development machine (Python 3.12) only offered `mlagents <= 0.28`, which does not support Unity 6, and torch / numpy / gymnasium were not installed. The observation / action interface (`BuilderSpec`) is kept stable so another trainer could plug in later.
- **In the game:** `BuilderController` loads the policy from Resources (falls back to a heuristic policy with a warning if it is missing or invalid) and uses the same observe / mask / apply code path as the evaluation episodes.
- **Balance tester:** `EpisodeRunner` plays a full game headless at a fixed time step (`SimClock`) with a configurable enemy-health scale and an allowed-turret-type filter.

### Reproduce

These are env-gated PlayMode tests (skipped in the normal test run). Run one at a time, since a project can only be open in one Unity process. Example (PowerShell):

```
Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform PlayMode -testCategory Training -testResults training.xml -logFile training.log
```

| Category | Enable with | Output |
|---|---|---|
| `Calibration` | `TD_CALIBRATE=1` (`TD_CAL_SEEDS`, `TD_HP_LO`, `TD_HP_HI`) | `docs/evaluation/calibration.md` |
| `Training` | `TD_TRAIN=1` (`TD_SEED`, `TD_GENERATIONS`, `TD_POP`, `TD_SIGMA`, `TD_LR`, `TD_BUDGET`, `TD_RESUME`) | `Assets/Resources/builder_policy.json`, `docs/evaluation/builder-training.csv` |
| `Evaluation` | `TD_EVAL=1` (`TD_BUDGET`, `TD_RESUME`) | `docs/evaluation/builder-eval.md` and `.json` |

Seeds: training episodes use seeds 1000-1999, evaluation uses seeds 1-30 (disjoint, asserted in the driver). The shipped policy was trained with seed 1, 30 generations, population 24, enemy HP band x1.3-1.4. See `docs/evaluation/builder-eval.md` for the full setup.

### Results

Evaluation on seeds 1-30 (N = 30 per cell). "HP x" is the multiplier on all enemy health (plus a +-5% per-seed jitter); all turret types allowed. Copied from section 1 of `docs/evaluation/builder-eval.md`:

| HP x | Allowed | Policy | N | Win rate [Wilson 95%] | Lives | Waves cleared | Return | Money spent | Turrets std/missile/laser | Wall s |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | all | Random | 30 | 0.733 [0.556, 0.858] | 6.40 +- 0.79 | 6.47 +- 0.47 | 169.7 +- 18.0 | 2012 | 16.9 / 0.7 / 0.5 | 28 |
| 1 | all | Heuristic | 30 | 1 [0.886, 1] | 9.00 +- 0.00 | 8.00 +- 0.00 | 230.0 +- 0.0 | 2550 | 23.0 / 1.0 / 0.0 | 35 |
| 1 | all | HeuristicTuned(standard) | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2400 | 24.0 / 0.0 / 0.0 | 34 |
| 1 | all | Trained | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2548 | 12.1 / 3.0 / 2.0 | 35 |
| 1.2 | all | Random | 30 | 0.6 [0.423, 0.754] | 4.47 +- 0.75 | 5.77 +- 0.51 | 133.5 +- 18.7 | 1735 | 14.1 / 0.7 / 0.5 | 27 |
| 1.2 | all | Heuristic | 30 | 1 [0.886, 1] | 3.37 +- 0.18 | 8.00 +- 0.00 | 173.7 +- 1.8 | 2540 | 22.9 / 1.0 / 0.0 | 40 |
| 1.2 | all | HeuristicTuned(standard) | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2400 | 24.0 / 0.0 / 0.0 | 39 |
| 1.2 | all | Trained | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2507 | 17.0 / 1.9 / 1.1 | 37 |
| 1.3 | all | Random | 30 | 0.533 [0.361, 0.698] | 3.50 +- 0.73 | 5.47 +- 0.51 | 115.7 +- 18.3 | 1600 | 12.7 / 0.7 / 0.5 | 26 |
| 1.3 | all | Heuristic | 30 | 0.6 [0.423, 0.754] | 1.33 +- 0.21 | 6.00 +- 0.45 | 103.3 +- 13.4 | 1763 | 15.1 / 1.0 / 0.0 | 28 |
| 1.3 | all | HeuristicTuned(standard) | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2400 | 24.0 / 0.0 / 0.0 | 38 |
| 1.3 | all | Trained | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2500 | 17.0 / 2.0 / 1.0 | 38 |
| 1.4 | all | Random | 30 | 0.367 [0.219, 0.545] | 1.63 +- 0.52 | 4.50 +- 0.50 | 75.5 +- 15.8 | 1243 | 9.1 / 0.7 / 0.5 | 21 |
| 1.4 | all | Heuristic | 30 | 0 [0, 0.114] | 0.00 +- 0.00 | 2.27 +- 0.08 | 11.3 +- 0.4 | 503 | 2.5 / 1.0 / 0.0 | 10 |
| 1.4 | all | HeuristicTuned(standard) | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2400 | 24.0 / 0.0 / 0.0 | 41 |
| 1.4 | all | Trained | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2513 | 17.0 / 1.7 / 1.3 | 40 |
| 1.5 | all | Random | 30 | 0.2 [0.095, 0.373] | 0.90 +- 0.43 | 3.70 +- 0.42 | 47.5 +- 13.1 | 932 | 6.1 / 0.7 / 0.5 | 16 |
| 1.5 | all | Heuristic | 30 | 0 [0, 0.114] | 0.00 +- 0.00 | 2.00 +- 0.00 | 10.0 +- 0.0 | 450 | 2.0 / 1.0 / 0.0 | 9 |
| 1.5 | all | HeuristicTuned(standard) | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2400 | 24.0 / 0.0 / 0.0 | 43 |
| 1.5 | all | Trained | 30 | 1 [0.886, 1] | 9.33 +- 0.14 | 8.00 +- 0.00 | 233.3 +- 1.4 | 2487 | 16.3 / 1.9 / 1.2 | 40 |
| 1.7 | all | Random | 30 | 0.033 [0.006, 0.167] | 0.33 +- 0.33 | 2.73 +- 0.22 | 20.3 +- 7.6 | 590 | 2.7 / 0.7 / 0.5 | 12 |
| 1.7 | all | Heuristic | 30 | 0 [0, 0.114] | 0.00 +- 0.00 | 2.00 +- 0.00 | 10.0 +- 0.0 | 450 | 2.0 / 1.0 / 0.0 | 9 |
| 1.7 | all | HeuristicTuned(standard) | 30 | 1 [0.886, 1] | 10.00 +- 0.00 | 8.00 +- 0.00 | 240.0 +- 0.0 | 2400 | 24.0 / 0.0 / 0.0 | 43 |
| 1.7 | all | Trained | 30 | 1 [0.886, 1] | 1.60 +- 0.19 | 8.00 +- 0.00 | 156.0 +- 1.9 | 2432 | 15.9 / 1.7 / 1.4 | 45 |

What this shows (details and caveats in the report):

- Trained beats Random at every HP scale and beats the plain Heuristic from x1.3 upward (the plain heuristic wastes money on Missile / Laser turrets).
- **Trained only ties the tuned heuristic** (HeuristicTuned, standard turrets only) on win rate at every scale (both 30/30) and is **worse on return at x1.5 and x1.7**. The learned policy does not beat a competent hand-written baseline, and that baseline sits at the score ceiling, so the matrix cannot show Trained being better.
- **Balance finding:** Standard turrets alone win 30/30 at x1.3 and x1.4 for every policy (even Random); Missile-only and Laser-only lose 30/30 at both scales for every policy. With current prices and enemy health the game is effectively solved by spamming the cheap Standard turret. The experiment cannot separate "too expensive" from "too weak".
- At the default scale x1.0 the shipped policy wins 30/30 with full lives.

### Limitations

- One scene and map, one deterministic game; episodes differ only by a +-5% enemy-HP jitter from the seed (and the Random policy's RNG).
- One training run (seed 1), narrow training band (x1.3-1.4), no variance across training runs.
- Ceiling effect: the best baseline hits the maximum return at all tested scales.
- The turret-type ablation is out-of-distribution for the Trained policy and mixes cost and power effects.

## Project layout

```
Assets/Scripts/                game scripts (Runtime assembly)
Assets/Scripts/AI/             builder agent: observation, actions, policies, ES trainer, episode runner, controller
Assets/Editor/SceneBuilder.cs  generates scene, prefabs, materials, waves
Assets/Resources/              trained policy (builder_policy.json)
Assets/Tests/EditMode/         unit tests
Assets/Tests/PlayMode/         full-game, determinism, auto-play tests and env-gated drivers
docs/evaluation/               calibration, training and evaluation reports
docs/plans/                    design plan
legacy/                        original pre-rewrite project files
```

## Tests

The Calibration / Training / Evaluation drivers are skipped unless their env var is set. Run from Window > General > Test Runner, or:

```
Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform EditMode -testResults editmode.xml
Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform PlayMode -testResults playmode.xml
```

At the time of writing: EditMode 54 pass; PlayMode 8 pass, 3 env-gated skipped. One PlayMode test auto-plays a full game with the trained policy and asserts a win with lives left.

## License

MIT, see [LICENSE](LICENSE).
