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
- **Training:** OpenAI-style Evolution Strategies (antithetic sampling, centered ranks, seeded), implemented in C# and run inside Unity in batch mode. Return = 100 * win + 10 * lives left + 5 * waves cleared. The shipped policy is generation 6 of 30, the first generation to reach the 240 ceiling (selected on 6 centre seeds inside the training range); later generations tie at the ceiling.
- **Why not ML-Agents:** `pip` on the development machine (Python 3.12) only offered `mlagents <= 0.28`, which does not support Unity 6, and torch / numpy / gymnasium were not installed. The observation / action interface (`BuilderSpec`) is kept stable so another trainer could plug in later.
- **In the game:** `BuilderController` loads the policy from Resources (falls back to a heuristic policy with a warning if it is missing or invalid) and uses the same observe / mask / apply code path as the evaluation episodes. After each AI build the shop selection is cleared.
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
| `RealTimeCheck` | `TD_RT=1` (`TD_BUDGET`, `TD_RESUME`) | `docs/evaluation/realtime-check.md` and `.json` |
| `EvalSpotCheck` | `TD_SPOT=1` | `docs/evaluation/spot-check.md` |

Seeds: training episodes use seeds 1000-1999, evaluation uses seeds 1-30 (disjoint, asserted in the driver). The shipped policy was trained with seed 1, 30 generations, population 24, enemy HP band x1.3-1.4. The HP-scale grid and the training band were chosen from a calibration run on seeds 1-10, which overlap the evaluation seeds; policy parameters and the tuned heuristic were selected on training seeds only. See `docs/evaluation/builder-eval.md` for the full setup.

### Results

Evaluation on seeds 1-30 (N = 30 per cell), in the headless fixed-step simulation (0.133 s per frame). "HP x" is the multiplier on all enemy health (plus a +-5% per-seed jitter); all turret types allowed. Copied from section 1 of `docs/evaluation/builder-eval.md`:

The full results table (24 rows, HP x1.0 to x1.7) is in [docs/evaluation.md](docs/evaluation.md).

What this shows (details and caveats in the report):

- Trained beats Random at every HP scale and beats the plain Heuristic from x1.3 upward (the plain heuristic wastes money on Missile / Laser turrets). It also has a higher return than the plain Heuristic at x1.0 and x1.2.
- **Trained only ties the tuned heuristic** (HeuristicTuned, standard turrets only) on win rate at every scale (both 30/30) and is **worse on return at x1.5 and x1.7**. The learned policy does not beat a competent hand-written baseline, and that baseline sits at the score ceiling, so the matrix cannot show Trained being better. The x1.5/x1.7 degradation may reflect the checkpoint choice (generation 6 of 30).
- **Balance finding (headless fixed-step simulation, 0.133 s per frame):** Standard turrets alone win 30/30 at x1.3 and x1.4 for every policy (even Random); Missile-only and Laser-only lose 30/30 at both scales for every policy (wave 3 reached, 2 waves cleared). With current prices and enemy health the game is effectively solved by spamming the cheap Standard turret in this simulation. The experiment cannot separate "too expensive" from "too weak".
- At the default scale x1.0, in the same simulation, the shipped policy wins 30/30 with full lives.
- **Validity checks:** a finer-step re-run (0.0167 s per frame; Trained and HeuristicTuned at x1.0 and x1.4, seeds 1-10) gives 10/10 wins with 10 lives and return 240.0 in all four cells, the same as the coarse step, so those conclusions do not change (`docs/evaluation/realtime-check.md`; N = 10, ceiling-bound). A re-run of 10 recorded evaluation episodes (Trained x1.4, HeuristicTuned x1.3, seeds 1-5) reproduced them exactly, 10 of 10 (`docs/evaluation/spot-check.md`; all at the 240 ceiling).

### Limitations

- One scene and map, one deterministic game; episodes differ only by a +-5% enemy-HP jitter from the seed (and the Random policy's RNG). The jitter is a deterministic per-seed sawtooth, and Trained / Heuristic / HeuristicTuned are deterministic, so the Wilson intervals describe the jitter population rather than sampling variance. Seeds are paired across policies, while the verdicts treat cells as independent (a conservative statement only).
- One training run (seed 1) and no second training seed, narrow training band (x1.3-1.4), no variance across training runs. The shipped checkpoint was chosen on 6 centre seeds in the training range; the stored `trainReturn` is selection-maximised (optimistic). The training code of `planCommit` 0aa7c6f was the working tree, not an exact commit.
- Ceiling effect: the best baseline hits the maximum return at all tested scales.
- The turret-type ablation is out-of-distribution for the Trained policy and mixes cost and power effects.
- Observation clamping: the per-type turret counts (/20) saturate at 24 turrets and HeuristicPolicy miscounts after 20 are built; the HP observation (/5000) is clamped and the HP multiplier is not observable. The policy file stores no slot-layout hash. Determinism is tested only for the pairs in the tests.
- All numbers are from the fixed-step simulation (0.133 s per frame); only four cells were repeated at a finer step.

## Project layout

```
Assets/Scripts/                game scripts (Runtime assembly)
Assets/Scripts/AI/             builder agent: observation, actions, policies, ES trainer, episode runner, controller
Assets/Editor/SceneBuilder.cs  generates scene, prefabs, materials, waves
Assets/Resources/              trained policy (builder_policy.json)
Assets/Tests/EditMode/         unit tests
Assets/Tests/PlayMode/         full-game, determinism, auto-play tests and env-gated drivers
docs/evaluation/               calibration, training, evaluation and validity-check reports
docs/plans/                    design plan
legacy/                        original pre-rewrite project files
```

## Tests

The Calibration / Training / Evaluation / RealTimeCheck / EvalSpotCheck drivers are skipped unless their env var is set. Run from Window > General > Test Runner, or:

```
Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform EditMode -testResults editmode.xml
Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform PlayMode -testResults playmode.xml
```

At the time of writing: EditMode 58 pass; PlayMode 10 pass, 5 env-gated skipped. One PlayMode test auto-plays a full game with the trained policy and asserts a win with lives left (an intentional regression gate for the shipped policy: it breaks on a retrain or rebalance by design).

## License

MIT, see [LICENSE](LICENSE).
