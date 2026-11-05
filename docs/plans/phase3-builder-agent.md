## Plan: Phase 3 - learned builder agent and balance tester

Form: full (ML design decision, new subsystem, a tooling constraint that changes the approach).

### Objective
Add an AI that decides which turret to build where, trained by reinforcement learning, with two uses:
(a) **auto-play** mode in the game, (b) **balance tester**: headless, fast-forward evaluation that reports win rate by policy, by allowed turret type, and by enemy-health scale. Evaluated against a random and a heuristic baseline on held-out seeds; results recorded in `docs/evaluation/`.

**Approach change vs the earlier idea (Unity ML-Agents + Python PPO):** `pip` on this machine's Python 3.12 only sees `mlagents <= 0.28`, which does not support Unity 6, and torch/numpy/gymnasium are not installed. So the learner runs **inside Unity in C#**: a small MLP policy trained with **OpenAI-style Evolution Strategies** (antithetic sampling, rank-shaped fitness, seeded). No downloads, runs in batch mode, fully verifiable here. The observation/action interface is kept stable so ML-Agents/PPO can replace the trainer later (see Open Questions).

### ML framing
- Decision supported: which of {none, standard, missile, laser} to place on which of the K=24 best build slots, every `decisionInterval` seconds of simulated time.
- Target / return: `R = 100*win + 10*livesLeft + 5*wavesCleared` (no shaping on money, so money hoarding is not rewarded).
- Metrics: win rate (Wilson 95% CI), mean lives left, mean waves cleared; over N episodes.
- Baselines: `RandomPolicy` (valid random actions) and `HeuristicPolicy` (nearest-to-path slots, cycles standard/missile/laser, spends when affordable - what the existing PlayMode bot does).
- Validation: train seeds 1000-1999 (with enemy-HP jitter 0.85-1.25), evaluation seeds 1-30 (disjoint), HP scales {0.8, 1.0, 1.2, 1.5}.
- Why ES: black-box, no gradients, tiny network (~1.4k params), needs only episode returns; parallelism not required.

### Reuse Audit
Exists: `BuildManager.BuildTurretOn(Node)`, `Node.turret`, `Shop` blueprints (costs), `PlayerStats`, `GameManager.gameEnded/Win/EndGame`, `WaveSpawner` (WaveNumber/TotalWaves), `EnemyRegistry`, `WayPoints.points`, PlayMode bot in `FullGameTests`.
Missing: observation builder, action decode/mask, policies, episode runner, ES trainer, JSON policy I/O, deterministic sim clock, enemy-HP multiplier hook, evaluation report writer, in-game AI toggle.

### File Structure
```
Assets/Scripts/AI/                         (in TowerDefence.Runtime asmdef)
  SimClock.cs               NEW  fixed-dt headless clock (Time.captureDeltaTime) + timeScale
  Difficulty.cs             NEW  static HealthMultiplier (applied in Enemies.Awake)
  BuilderSpec.cs            NEW  K slots, action layout, obs size (single source of truth)
  BuilderObservation.cs     NEW  builds float[] obs from live game state
  BuilderActions.cs         NEW  mask + apply action via BuildManager; allowed-type filter
  IBuilderPolicy.cs         NEW  int Act(float[] obs, bool[] mask)
  RandomPolicy.cs / HeuristicPolicy.cs / MlpPolicy.cs   NEW
  PolicyIO.cs               NEW  JSON save/load incl. metadata (seed, versions, config)
  BuilderController.cs      NEW  MonoBehaviour: runs a policy in the scene (auto-play)
  EpisodeRunner.cs          NEW  runs one episode, returns EpisodeResult
  EvolutionStrategy.cs      NEW  OpenAI-ES (pure C#, seeded System.Random)
Assets/Scripts/Enemies.cs   MOD  health *= Difficulty.HealthMultiplier in Awake
Assets/Editor/SceneBuilder.cs MOD add AI toggle button + BuilderController
Assets/Resources/builder_policy.json  NEW (trained weights + metadata)
Assets/Tests/EditMode/AiTests.cs      NEW
Assets/Tests/PlayMode/AiPlayTests.cs  NEW (determinism, auto-play, training+eval drivers)
docs/evaluation/builder-<date>.md + .json NEW
docs/plans/phase3-builder-agent.md    this file
README.md                  MOD  AI section (real numbers only)
```

### Design / Data Flow
- **SimClock**: `Time.captureDeltaTime = 1/30`, `timeScale = 4` => each frame is a fixed 0.133 s of game time, run as fast as the CPU allows. Deterministic given seed.
- **Episode**: load `Main`, set `Difficulty.HealthMultiplier` from the episode seed, loop: every `decisionInterval` (1.0 s sim) build obs -> mask -> `policy.Act` -> apply. Ends when `GameManager.gameEnded` or sim-time cap (600 s). Restore clock after.
- **Slots**: the K=24 nodes closest to the path (stable order by distance then name).
- **Action space**: `0 = wait`, else `1 + slot*3 + type` (73 actions). Mask: slot free, enough money, type allowed.
- **Observation (~36)**: money/1000, lives/10, wave/total, spawning flag, enemies alive/50, remaining enemy HP/5000, per-type turret counts/20 (3), affordability flags (3), slot occupancy (24).
- **MlpPolicy**: obs -> 24 tanh -> 73 logits, masked argmax (training: argmax is fine for ES; sampling optional).
- **ES**: population 24 (12 antithetic pairs), sigma 0.1, lr 0.05, generations G (default 30), fitness = mean R over 2 episodes per candidate, centered ranks. Logs mean/best per generation to a CSV.
- **Balance tester**: evaluation matrix = policies {Random, Heuristic, Trained} x allowed types {all, std, missile, laser} x HP scale {0.8, 1.0, 1.2, 1.5}, 30 seeds each (Random/Heuristic/Trained on `all` only for the HP sweep to bound runtime).

### Acceptance Criteria
- [ ] EditMode: obs length == `BuilderSpec.ObsSize`; mask forbids occupied slots, unaffordable and disallowed types; `Act` never returns a masked action. `Unity -runTests -testPlatform EditMode` all green.
- [ ] EditMode: `MlpPolicy` -> JSON -> `MlpPolicy` round-trips bit-identical outputs; ES maximises a toy quadratic (return improves by > 90% of the gap in 50 iterations).
- [ ] PlayMode: the same policy + seed run twice gives identical `EpisodeResult` (determinism test).
- [ ] PlayMode: `HeuristicPolicy` episode terminates (win/lose) within the sim cap with no error logs; existing 2 PlayMode tests still pass.
- [ ] Training run (`TD_TRAIN=1`, PlayMode category `Training`) completes, writes `Assets/Resources/builder_policy.json` with metadata (seed, generations, Unity version, config) and `docs/evaluation/builder-training.csv`.
- [ ] Learning check: mean return on eval seeds of Trained > Random, with the difference larger than the 95% CI half-width. If not met, the report states it plainly and the criterion is marked NOT MET.
- [ ] Evaluation report `docs/evaluation/builder-<date>.md` lists the full matrix, N, seeds, CIs, Unity/package versions, runtime; Trained vs Heuristic is reported whichever way it falls.
- [ ] In-game: scene has an "AI" toggle; PlayMode test enables auto-play with the trained policy and the game reaches an end state with no error logs.
- [ ] `git diff --check` clean; nothing in `Library/`, `UserSettings/` tracked; README numbers equal report numbers.

### Risks
- Non-determinism (variable dt, physics, Destroy timing) -> fixed dt via `captureDeltaTime`; determinism test is a gate; if it fails, evaluate with more seeds and report the variance instead of claiming exactness.
- ES learns slowly or overfits the HP jitter -> disjoint eval seeds, HP sweep, report honestly; fall back to the heuristic as the shipped default if trained is worse.
- Reward hacking (e.g. stalling) -> reward only from outcome; sim-time cap counts as a loss.
- Runtime too long -> episode ~ 1-3 s headless; budget in the report; G/population configurable; run in background.
- Editor/runtime split: all AI code is in the Runtime asmdef; the trainer is only invoked from PlayMode tests (no `UnityEditor` references in runtime code).
- `Time.captureDeltaTime` left set after a failure -> restored in `finally`/TearDown.
- Unity project lock: only one Unity process at a time on this project.

### Reviews
- `ml-systems-reviewer` (evaluation validity, seed separation, leakage between train/eval, determinism, reward hacking) - required.
- `code-reviewer` (C# bugs, static state, test quality) - required. No third reviewer.

### Implementation Order
1. **Sim core**: `SimClock`, `Difficulty` (+Enemies hook), `BuilderSpec/Observation/Actions`, policies (Random, Heuristic, Mlp), `PolicyIO`, EditMode tests. Verify: EditMode green; existing PlayMode tests still green.
2. **Episodes**: `EpisodeRunner`, `EpisodeResult`, determinism + heuristic episode PlayMode tests. Verify: those tests green; log of 5 heuristic episodes (time/episode).
3. **Training**: `EvolutionStrategy`, training driver test (env `TD_TRAIN=1`, `TD_GENERATIONS`, `TD_POP`, `TD_SEED`), run it, commit policy JSON + CSV.
4. **Evaluation / balance**: matrix driver test (env `TD_EVAL=1`), run it, write `docs/evaluation/builder-<date>.{md,json}`.
5. **In-game AI mode**: `BuilderController`, `SceneBuilder` toggle, rebuild scene, auto-play PlayMode test, README section.
Run commands (PowerShell): `Unity.exe -batchmode -nographics -projectPath <repo> -runTests -testPlatform PlayMode -testCategory "Training" -testResults <xml> -logFile <log>`.

### Open Questions
1. **Trainer choice (assumption made):** in-Unity ES instead of ML-Agents PPO because Python 3.12 cannot install a Unity-6-compatible `mlagents`. If you want PPO anyway: needs Python 3.10 (~25 MB) + PyTorch CPU (~200 MB) downloaded, plus the `com.unity.ml-agents` package version check for Unity 6000.6 - requires your approval for the downloads.
2. Reward weights (100/10/5) and K=24 slots are my choices; adjust if you prefer.
3. Evaluation budget: 30 seeds per cell; raise if the CIs are too wide to separate policies.
4. Does "win rate per turret type" mean ablation (only that type allowed)? Assumed yes (no per-kill attribution in the game code).
5. **Calibration result (Phase 3 step A):** `docs/evaluation/calibration.md`. At HP x1.0 the heuristic wins always; at x2.0+ every policy loses every episode; the cliff is between x1.3 and x1.4 (heuristic 100% -> 0%, Random 50% -> 20%). No band separates the policies cleanly; training uses HP jitter 1.3-1.4 (heuristic near 50%) instead of 0.85-1.25 from the plan. Evaluation HP scales in Phase 4 should be revisited accordingly (e.g. {1.0, 1.2, 1.3, 1.4}).
6. **Post-review notes (results as built):** `docs/evaluation/builder-eval.md` has the full provenance. In short: the HP-scale grid and the training band came from a calibration run on seeds 1-10, which overlap the evaluation seeds 1-30; policy parameters and the HeuristicTuned variant were selected on training seeds only. The shipped policy is generation 6 of 30 (first to reach the 240 ceiling, chosen on 6 centre seeds in the training range; training saturates from about generation 18), and `planCommit` 0aa7c6f is the first commit of the range, the training code being the working tree at that time. All results hold for the headless fixed-step simulation (0.133 s per frame); `docs/evaluation/realtime-check.md` repeats four cells at 0.0167 s per frame (no change) and `docs/evaluation/spot-check.md` re-runs 10 recorded episodes (10/10 identical). Known limitations: observation clamping (type counts /20 saturate at 24 turrets, HP obs /5000 clamped, HP multiplier not observable), no slot-layout hash in the policy file, determinism tested only for the pairs in the tests, a single training seed.
