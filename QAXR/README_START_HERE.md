# QAXR - Human-like Simulated Agent Test (Unity 6, Scenario 01)

QAXR (Quality Assurance in Extended Reality) uses simulated agents to explore a VR scene, complete a task, and
produce motion and interaction data for five cybersickness risk dimensions (D1-D5).

> **Important.** The agent is a *plausible simulated VR user built from behavioural heuristics*. It is **not** a validated
> model of human cognition, perception, sickness or physiology. Every probability, speed and duration in this project is an
> **illustrative, uncalibrated** parameter. The profiles (Focused / Exploratory / Cautious) are **configurations**, not
> empirically derived human categories. Head direction is a **simulated head pose, not eye gaze** (Meta Quest 3 has no eye tracking).

---

## 1. Quick start

1. Open the project in Unity 6 (6000.3.x) and open `Assets/Scenes/QAXR_Demo.unity`.
2. Press **Play** and watch the **Game** tab. A Baseline run (Focused profile, seed 42) starts automatically.
3. Use the on-screen panel:

| Button | Effect |
|---|---|
| Run Baseline / Run Fault | Same task and seed, without or with the injected forced camera roll |
| A/B Pair | Baseline, then Fault with the same seed. Reports whether the agent's decisions were IDENTICAL |
| Replay (same seed) / New seed | Reproduce exactly / vary the stochastic behaviour |
| Profile: X | Cycle Focused, Exploratory, Cautious (restarts the run) |
| Agents: N | Cycle 1, 3, 5 agents (restarts the run) |
| Next agent | Choose which agent the panel and Agent View follow |
| Agent View / Overview | Look through the selected agent's camera |
| Path / Head / Perception / Target / All | Debug drawing toggles |

4. **QAXR -> Run Validation Tests** runs all acceptance tests headlessly (about 1 minute) and writes
   `QAXR_Runs/validation/validation_report.txt`.
5. **QAXR -> Rebuild Demo Scene** regenerates the scene from code.

## 1b. Viewing in VR with Meta Quest 3

The project uses **OpenXR** (`com.unity.xr.openxr` 1.18.0 + XR Plug-in Management 4.7.0). Everything is already set up:
- OpenXR loader enabled for Windows and Android, starting automatically when you press Play.
- Oculus Touch and Meta Quest Touch Plus controller profiles enabled.
- Meta Quest Support enabled for Android.

To redo this setup, use **QAXR -> Configure VR (Meta Quest 3)**.

**Option A - PC + Quest Link (fastest, works in Play mode)**
1. Install the **Meta Quest Link** app on the PC and sign in.
2. In Link, go to Settings > General and set **OpenXR Runtime = Meta Quest Link** ("Set Meta Quest Link as active").
3. Connect the Quest 3 with a USB-C cable (Link) or use Air Link, and enter Link mode in the headset.
4. Press **Play** in Unity. You will appear on the observer platform beside the corridor.
   - The desktop window keeps the control panel for an operator.
   - With no headset connected, the project simply runs in desktop mode.

**Option B - Standalone on the headset (Android build)**
1. Enable Developer Mode for the Quest 3 in the Meta Horizon phone app, then connect the headset by USB.
2. File > Build Profiles > **Android** > Switch Platform. IL2CPP, ARM64, API 32 and Vulkan are already configured.
3. **Build And Run**. CSV files are written to the app's storage on the headset (`Android/data/<package>/files/QAXR_Runs`).

**Controller buttons**

| Button | Action |
|---|---|
| **A / B** (right) | Next / previous view |
| **X** (left) | Agent-view monitor on/off (a small screen above the left controller) |
| **Y** (left) | Help panel on/off |
| Left trigger / Right trigger | Run Baseline / Run Fault |
| Right stick press | A/B pair |
| Left stick press | Change profile |
| Right grip / Left grip | Next agent / pause |
| Keyboard (operator) | V view, M monitor, H help, B baseline, F fault, P pair, N agent, C profile |

**Views**
1. **Observer platform:** elevated, outside the corridor; you see the whole test. The most comfortable view.
2. **Fault zone side:** standing in Zone B next to the path; agents walk past you.
3. **Follow agent:** beside the selected agent. Re-positions in short fade "snaps" instead of sliding.
4. **Ride-along:** you move with the agent. Only position follows; you control your own head rotation.

**Comfort / safety**
- The injected forced roll is a sickness-risk stimulus. It is **never applied to your headset view by default**.
  To see it, use the **agent-view monitor (X)**, a small screen showing exactly what the agent camera sees.
- `QAXRVRRig.applyInjectedRollInRideAlong` exists for deliberate testing but is off. Enable it only with care.
- Ride-along involves visual motion while you stand still. Stop if you feel unwell.

**Measurement isolation.** The VR rig:
- has no perceivable component and no colliders, and sits on the Ignore Raycast layer;
- is excluded from agent cameras;
- is never read by the agents.

So agent decisions and D1-D5 data are the same with or without the headset. The full validation suite still passes with the rig in the scene.
In VR, `frame_time_ms` and `fps` reflect the headset-driven frame loop (Editor + Link). They are **not** standalone Quest performance.

## 2. What happened to the old agent (and why it changed)

| Previous agent | Limitation | Now |
|---|---|---|
| Fixed probabilities Walk 60 / LookAround 20 / Idle 10 / Turn 10 every second | Same behaviour in an empty corridor and next to a panel | Task rules plus context-dependent utilities |
| Waypoints with a "passed z" rule, fixed interaction timer | No notion of subgoals or completion conditions | Layer A subgoals with completion conditions and timeouts |
| No perception, no obstacles | Could not react to the environment | View-cone and occlusion perception, obstacle avoidance |
| Head jumped to targets at constant speed, continuous oscillation possible | Robotic motion | Smoothed, rate-capped head with glance and pause sequences |
| Camera roll on the camera itself | Head and app motion only separated by convention | Explicit `HeadPivot -> ApplicationCameraOffset -> AgentCamera` |

## 3. Architecture

```
            +----------------------- QAXRAgentController (glue, one per agent) ----------------------+
 Scene ---> | Perception          Layer A                Layer B                     Layer C          |
 objects    | QAXRPerception  ->  QAXRTaskPlanner    ->  QAXRBehaviorSelector   ->   QAXRMotionController|
            | view cone,          mission = subgoals,    task rules + utility        smooth body/head |
            | occlusion,          completion checks,     sampling (own RNG)          rate limits      |
            | memory (novelty)    timeouts / failures                                                 |
            +------------------------------------------------------------------------------------------+
                         |  writes AgentRoot, Body, HeadPivot only
 QAXRScenarioController: fixed step loop, zones, fault -> ApplicationCameraOffset, multi-agent, UI
 per agent: QAXRMetricsCollector (D1-D5 CSV + config) and QAXRRiskDetector (rule, episodes)
```

**Why this design.** We use a hierarchical model: a task state machine (Layer A) sits on top of rule-plus-utility action
selection (Layer B), which drives rate-limited motion primitives (Layer C).
- It is **explainable**: every decision is logged with its trigger, the probabilities of each candidate action and the random draw.
- It is **calibratable**: utilities and durations are plain parameters that can later be fitted to human data.
- It is **deterministic**: there is one RNG per agent and a fixed step.
- It is **cheap**: no ML, no LLM.

A behaviour tree would also work. Utility selection was chosen because it expresses "more likely in this context" directly,
which is exactly what the profiles need.

### Transform hierarchy (keeps D2 motion sources separate)

```
AgentRoot (position)                      <- agent (walking)
 +- Body (yaw)                            <- agent (body turning)
     +- Body Visual (capsule)
     +- HeadPivot (yaw/pitch)             <- agent (simulated head movement, NOT eye gaze)
         +- Visor
         +- ApplicationCameraOffset       <- scenario only (injected forced roll = application-controlled)
             +- AgentCamera (FOV 85)      <- measured by the metrics collector
```

Perception uses the **HeadPivot**, not the camera, so the injected camera fault can never influence what the agent perceives or decides.

### Layer A - task planning (`QAXRTaskPlanner`, mission editable on the agent in the Inspector)

| # | Subgoal | Completion condition | Timeout |
|---|---|---|---|
| 1 | NavigateToTarget "Enter corridor" | passed z = 2 m | 30 s |
| 2 | NavigateToTarget "Navigate through Zone A" (crate in the way) | passed z = 7.3 m | 40 s |
| 3 | NavigateToTarget "Cross Zone B" | passed z = 13.8 m | 40 s |
| 4 | ObserveTarget "Find the control panel" | panel visible in the view cone AND head on it for 0.4 s | 40 s |
| 5 | ApproachTarget | within interaction range (0.95 m) AND speed < 0.05 m/s AND facing within 20 deg | 40 s |
| 6 | InteractWithTarget | align, hesitate, press (range checked), observe result | 30 s |
| 7 | NavigateToExit | within 0.35 m of the exit point | 40 s |

A missing target, an expired timeout, or repeated no-progress (stuck detection) ends the run with a logged
`MissionFailed` reason.

### Layer B - behaviour (`QAXRBehaviorSelector`)

**Tier 1, task rules (no randomness unless stated):**
- Interact subgoal -> Interact.
- Approach -> LookAtTarget(panel). The agent slows down and aligns. If another agent is using the panel, it Stops and waits.
- Observe with the panel visible -> LookAtTarget(panel). The first sighting may cause a Stop, with probability noveltyCaution x 0.6.
- After Stop, Inspect or Interact -> ResumeNavigation.

**Tier 2, utility sampling.** Utility = base weight x context multipliers. One action is drawn in proportion to utility, using the agent's RNG.

| Action | Context multipliers / eligibility |
|---|---|
| Walk | base only |
| SlowDown | + sharp turn ahead, + obstacle within 2.5 m, + novel object x noveltyCaution |
| Stop | + novel object x noveltyCaution. Not near obstacles. Capped total stop time |
| LookAtTarget | uninspected landmark visible, x (0.5 + curiosity) |
| LookAround | 0 while approaching a target. x0.3 near obstacles. x3 while searching for the panel |
| TurnBody | bounded detour, only in open space, more than 4 m from the goal, profile maxDetourOffset > 0 |
| InspectObject | uninspected landmark within 4 m, x (1 + 3 x curiosity). Agent stops; body partly turns |
| (all non-walk) | x0.1 within minActionGap after the last action; x repetitionPenalty if the same as the previous action |

**Decisions are event-driven:** at the end of the current action, on a subgoal change, a new object noticed, an obstacle
within 2.5 m, or the task target becoming visible.

### Layer C - motion (`QAXRMotionController`)

- **Walking:** speed is smoothed (SmoothDamp) and hard-capped at maxAcceleration / maxDeceleration. It is reduced for sharp
  turns (1 / (1 + (err / turnSlowdownAngle)²)). It brakes progressively within slowdownDistance of the panel and the exit.
  Speed varies by ±speedVariation at each decision.
- **Body:** SmoothDampAngle, hard-capped at turningSpeed. The agent turns in place to align at the panel.
- **Obstacles:** tangent steering around the obstacle's clearance circle, with a sticky side choice. Corridor bounds and
  footprint constraints are the last-resort safety net; tests show they are never needed (0 corrections).
- **Head:** a separate pivot, SmoothDampAngle capped at headMaxSpeed, limits of ±75 deg yaw and -30/+40 deg pitch.
  Anticipatory look into turns. Glance, hold, pause sequences. No unprompted oscillation.

## 4. Behaviour profiles (illustrative configurations)

| Parameter | Focused | Exploratory | Cautious |
|---|---|---|---|
| preferredSpeed (m/s) | 1.45 | 1.25 | 0.95 |
| speedVariation | 0.04 | 0.08 | 0.05 |
| maxAcceleration / maxDeceleration (m/s²) | 1.4 / 1.8 | 1.2 / 1.6 | 0.8 / 1.2 |
| turningSpeed (deg/s) | 100 | 90 | 60 |
| slowdownDistance (m) / turnSlowdownAngle (deg) | 1.6 / 70 | 2.0 / 60 | 3.2 / 35 |
| headMaxSpeed (deg/s), glances per look-around | 120, 1 | 140, 2-3 | 90, 1 |
| wWalk / wLookAround / wInspect / wStop / wTurnBody | 10 / 0.5 / 0.2 / 0.15 / 0 | 6 / 3 / 3 / 0.5 / 1 | 10 / 0.5 / 0.4 / 1.0 / 0 |
| curiosity / noveltyCaution | 0.1 / 0.05 | 1.0 / 0.2 | 0.3 / 1.0 |
| minActionGap (s) | 3.0 | 1.0 | 1.5 |
| maxDetourOffset (m) | 0 | 0.6 | 0 |
| interaction hesitation / result observation (s) | 0.2-0.4 / 0.5-0.8 | 0.3-0.6 / 1.0-1.8 | 0.8-1.5 / 1.2-2.0 |

All fields are in `QAXRBehaviorProfile`. To set your own values, choose `profileType = Custom` on the agent and edit `customProfile`.
The scenario's `profile` field (or the Profile button) sets agent A01. Extra agents use `extraAgentProfiles`.

## 5. Scene - Scenario 01 VR Training Corridor

| Zone | z (m) | Contents | Purpose |
|---|---|---|---|
| A Normal Movement | 0-8 | Supply crate obstacle (z 5), safety poster, info screen | Baseline motion, obstacle avoidance, landmark inspection |
| B Fault Zone | 8-14 | Fire extinguisher landmark, warning stripes | Forced roll, ±20 deg at 1.2 Hz for 4.17 s (Fault runs only) |
| C Interaction Zone | 14-23.5 | Plant landmark, control panel (z 19), exit door | Find, approach and press the panel, then exit |

Perceivable objects carry `QAXRPerceivable` (kinds: Waypoint, Obstacle, ControlPanel, Landmark, Exit, Agent).
These components contain no fault or risk data.

## 6. Reproducibility and A/B testing

- **Fixed simulation step:** 0.02 s. CSV sampling is separate, at 0.1 s.
- **Random generators:**
  - Each agent has its own `System.Random(seed + index x seedStride)`. Unity's global `Random` is never used.
  - The environment has a separate generator (`environmentSeed`). It is used only for the optional `faultOnsetJitter`, which is 0 by default.
- **The fault writes only `ApplicationCameraOffset`.** The agent never reads it, so Baseline and Fault runs with the same
  seed produce identical decisions and body/head trajectories. Tests 5 and 6 verify this with hashes.
- **Every decision is logged:** trigger, probabilities, random draw, duration and speed scale. Each run's
  `decision_hash` appears in the summary and config.
- **No recorded-action replay is needed,** because exact determinism holds.
  - **Caveat:** it holds on the same Unity version and platform. Floating-point differences across CPUs or Unity versions
    could in principle change a near-tie decision.
  - **Detecting a mismatch:** compare decision hashes. The decision log contains enough to rebuild a replay mechanism if
    that is ever needed.

## 7. Data output (`%USERPROFILE%\AppData\LocalLow\DefaultCompany\QAXR_Complete_Unity6_Project\QAXR_Runs`)

Per run and agent: `<run>_<agent>_samples.csv`, `_events.csv`, `_summary.csv`, `_config.json`. A table of all runs is
appended to `QAXR_all_runs_summary.csv`, or to `_v2.csv` if an older file with a different header exists.

**Samples: the first 34 columns are unchanged in order and units.** One change in *values* to note:
`agent_state` now contains the Layer B action names.
- Walk, SlowDown, Stop, LookAtTarget, LookAround, TurnBody, InspectObject, Interact, ResumeNavigation, Finished, Failed.
- Old Idle corresponds to Stop. Old Turn corresponds to TurnBody.

Appended v2 columns:

| Column | Dim | Meaning |
|---|---|---|
| profile | - | behaviour profile name |
| subgoal | - | current subgoal, e.g. `5/7 ApproachTarget (Approach the control panel)` |
| attention_target | D2/D3 | what the simulated head is oriented to (not gaze) |
| head_yaw_rel_deg, head_pitch_rel_deg | D1/D2 | head pose relative to the body |
| nearest_obstacle_m | D3 | distance to the nearest perceived obstacle or agent surface |
| decision_id | - | number of decisions so far (joins to events) |
| agent_sim_ms | D4 | CPU time of this agent's simulation step, i.e. test-harness overhead, kept separate from frame_time_ms |
| fps | D4 | Editor frames per second (not Quest) |

**Events:**
- RunStart, AgentConfig
- Decision (with p=[...] and draw=), AgentState
- SubgoalStart / SubgoalComplete
- ObstacleAvoidance, ZoneEnter
- FaultArmed / FaultStart / FaultEnd, RiskFlagStart / RiskFlagEnd
- InteractionPhase, ButtonPressed
- StuckDetected, MissionFailed, MissionComplete, AgentExited, RunEnd

**Summary:**
- The v1 columns are unchanged. `idle_*` now counts Stop, and `turn_count` counts TurnBody.
- Appended columns: profile, outcome, decisions, decision_hash, action counts, mean speed, head travel, obstacle
  corrections, start delay, agent index/count, agent sim ms per step.

**Config JSON:** seed, environment seed, the full profile, perception and motion parameters, the mission, the timing,
fault parameters, the risk threshold, the Unity version and the decision hash.

## 8. Multiple agents

Set `agentCount` (1-5) or use the Agents button.
- **Spawning:** extra agents are clones of A01, each with its own profile, seed, start delay (4 s stagger), task state,
  head/body transforms, run ID and CSV files.
- **Interaction between agents:** agents perceive each other as moving obstacles and wait if the panel is occupied.
  Agents leave the scene when they exit.
- **D4:** `agent_sim_ms` separates the agents' CPU cost from the frame time of the tested scene.

## 9. Validation results (QAXR -> Run Validation Tests)

Last run: **10 / 10 passed**.

| Test | Result |
|---|---|
| 1 Task completion | 24/24 runs (3 profiles x 8 seeds) completed, including the button press |
| 2 Context-aware | Empty-corridor walking speed vs final 2 m of panel approach: Focused 1.41 vs 0.84, Exploratory 1.17 vs 0.69, Cautious 0.90 vs 0.44 m/s. Head on panel 100% of the approach. 0 random looks/turns during approach |
| 3 Smooth motion | Acceleration, body turn and head rates never exceed their caps. No position jumps. Largest camera yaw change 4.6 deg per 0.02 s step |
| 4 Profiles | Mean duration 21.5 / 30.3 / 38.5 s. Moving speed 1.21 / 0.93 / 0.70 m/s. Head travel 414 / 934 / 530 deg. Exploratory actions 1.1 / 4.6 / 1.6 (Focused / Exploratory / Cautious) |
| 5 Replay | Identical decision and trajectory hashes for the same seed, all profiles |
| 6 A/B | Identical decisions and trajectories. Max roll rate 0 vs 151 deg/s. Risk episodes 0 vs 1 |
| 7 Measurement integrity | 0 D2 label errors. Camera roll = app roll exactly. Camera yaw rate = body + head yaw rate |
| 8 Export | v1-compatible header plus v2 fields. Events include decisions with random draws. Config contains everything needed to reproduce the run |
| 9 Robustness | An unreachable point fails after 20 s with a stuck reason. A missing target fails immediately. Agents never leave the corridor. Crate avoided with 0 constraint corrections |
| Multi-agent | 3/3 and 5/5 agents completed in a fault run. Minimum separation 0.76 m |

## 10. Limitations and open issues

- **Not validated:** behaviour rules, profile values and the 60 deg/s risk threshold are heuristics, not calibrated against
  human data. Calibration would need recorded human trajectories (for example head and controller pose logs from a Quest 3).
- **No skeleton:** there is no skeletal animation and no gait bob. Head height is constant (no vertical head motion from walking).
- **Navigation:** local tangent steering plus waypoints, not a global planner (no NavMesh). It is fine for corridors.
  Complex mazes would need a path planner on top of Layer C.
- **Perception:** horizontal view cone plus raycast occlusion. There is no visual saliency or optic-flow model, and no eye gaze.
- **Locomotion:** only continuous walking. Teleport and snap-turn are not implemented yet; they would be new Layer C primitives.
- **D3 / D4:** no vignette yet (the column is reserved). Frame time is Editor-only. Quest runtime metrics need on-device profiling.
- **Multi-agent:** agents avoid each other locally, but there is no crowd model. Agents queue at the panel by simple waiting.
- **Determinism:** guaranteed per Unity version and platform (see section 6).

## 11. Scripts

| Script | Role |
|---|---|
| `QAXRAgentController.cs` | Glue for perception, Layers A/B/C, interaction sequence, stuck detection, decision logging and hash |
| `QAXRTaskPlanner.cs` | Layer A: subgoals, completion conditions, timeouts, failures |
| `QAXRBehaviorSelector.cs` | Layer B: task rules plus context-aware utility sampling |
| `QAXRMotionController.cs` | Layer C: smooth, rate-limited body and head motion, constraints |
| `QAXRPerception.cs` / `QAXRPerceivable.cs` | View cone, occlusion, proximity, novelty memory / perceivable object tags |
| `QAXRBehaviorProfile.cs` | Profile parameters and the Focused, Exploratory and Cautious presets |
| `QAXRScenarioController.cs` | Fixed-step loop, zones, fault on ApplicationCameraOffset, multi-agent, A/B, UI, CSV/JSON |
| `QAXRMetricsCollector.cs` | D1-D5 measurement and export (v1 schema plus appended v2 fields) |
| `QAXRRiskDetector.cs` | Demo risk rule and episodes (unchanged) |
| `QAXRDebugVisualizer.cs` | Toggleable path, head, perception and target lines (not visible to agent cameras) |
| `QAXRControlPanel.cs` | Panel and button, occupancy |
| `QAXRSceneBuilder.cs` | Builds corridor, obstacle, landmarks, panel, mission and agent hierarchy |
| `Editor/QAXRValidation.cs` | Acceptance tests 1-9 plus multi-agent |
| `QAXRVRRig.cs` | Meta Quest 3 viewer: head and controller tracking, 4 views, buttons, wrist panel, agent-view monitor, comfort fade |
| `Editor/QAXRVRSetup.cs` | One-click OpenXR / Quest configuration (QAXR > Configure VR) |
| `Editor/QAXRFirstOpenSetup.cs` | First-open scene generation and the Rebuild menu |
