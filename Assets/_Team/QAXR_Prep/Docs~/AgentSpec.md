# W1: Simulated agent spec (v1, draft for Wanglei + Zihan)

One page. It fixes what an agent takes in and what it writes out, so W3 (MotionRecorder), J6 (Replay), W4 (scoring) and the Python analysis can be built in parallel. Code that already implements it: `Runtime/Trace.cs`, `Runtime/AgentMath.cs`, `Runtime/Risk.cs`.

## Inputs (per agent)

| Input | Type | Default | Notes |
|---|---|---|---|
| `mode` | `Path` / `Explore` | `Path` | Path: seeded walk over NavMesh waypoints. Explore: the intent Markov chain (§4.2) |
| `seed` | int | `baseSeed + agentIndex` | Logged in every issue and in the trace header. Same seed gives the same intent sequence (physics may still differ) |
| `embodiment` | `Ghost` / `Embodied` | `Ghost` | Ghost moves its own origin and copies the providers' settings. Embodied drives the real XR Origin through the app's locomotion providers |
| `profile` | `AgentProfile` asset | `Adult` | Stature, eye height, arm reach, walking speed, head yaw/pitch limits, allowed locomotion, dwell median, handedness |
| `chaos` | 0–1 | 0.1 | 0 = realistic user, 1 = monkey (uniform over valid intents) |
| transition rows | `Func<Intent, bool holding, float[]>` | `TransitionTable.Default` | 11 × 11, rows by (intent, holding); invalid intents get 0 |
| locomotion | read from the scene each frame | | Move speed, turn speed, snap angle, vignette on/off. Zones may override through `QaxrOrigin` |

## Per-frame outputs

The agent exposes these every frame, and `MotionRecorder` reads them. A real user exposes the same fields, so one recorder serves both.

- `origin` and `head` transforms (head local to origin), plus left and right hand transforms
- `LocoCommand` flags for this frame: what locomotion the agent or user *asked for* (`Move`, `Turn`, `SnapTurn`, `Teleport`). D2 "uncontrolled motion" means the origin moved while this was `None`. Teleport and SnapTurn frames are excluded from the D1 speed, acceleration and yaw-rate rules
- current `Intent`, current `target` (GameObject name or empty), `held` object (name or empty)
- select and activate state per hand

## Per-event outputs

Issues go through `IssueSink.Report(Issue)` (J1's `IssueLog.Report` later). The fields are those of §4.6: `id, dimension, rule, severity, value, threshold, unit, position, time, agent, seed, profile, intent, scene, commit`.

## Trace file

- **Path:** `persistentDataPath/qaxr/<run>/agent_<id>_trace.csv`, with `<run>` = `yyyyMMdd_HHmmss`.
- **Rate:** 30 Hz (`TraceWriter.Hz`). Sample on `Time.time` and skip frames that come early; never interpolate on write.
- **Header:** `#`-comment lines `# key=value`: `format=qaxr_trace_v1, run, agent, seed, profile, mode, embodiment, chaos, scene, commit, hz`. pandas reads it with `comment='#'`.

| Column | Unit | Meaning |
|---|---|---|
| `t` | s | Time since the run started (`Time.time` − start; scaled time, so it stays right under `timeScale` 2–4) |
| `intent` | name | `Intent` enum name |
| `target` | name | Current target, empty if none |
| `held` | name | Held object, empty if none |
| `loco` | int flags | 0 none, 1 Move, 2 Turn, 4 SnapTurn, 8 Teleport |
| `origin_px, origin_py, origin_pz` | m | Origin world position |
| `origin_qx, origin_qy, origin_qz, origin_qw` | | Origin world rotation |
| `head_px … head_qw` | m, quaternion | Head pose **local to the origin** (= tracked pose for real users) |
| `lhand_px … lhand_qw`, `rhand_px … rhand_qw` | m, quaternion | Hand poses local to the origin |
| `sel_l, sel_r, act_l, act_r` | 0/1 | Select and activate state per hand |

World head pose = origin · head. All numbers use the invariant culture (`.` as decimal separator), positions with 4 decimals and quaternions with 5.

## Not in v1

Eye gaze, controller buttons other than select/activate, and physics state. Add columns at the end only, and bump `format` when you do.
