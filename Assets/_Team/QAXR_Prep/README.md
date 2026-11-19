# QAXR_Prep

These are the standalone pieces of the QAXR roadmap that don't depend on anything else. The folder has its own asmdefs and uses namespace `QAXR`. It depends only on the XRI, XR Core Utils, Input System and TMP packages, not on the template. Merge it into the real plugin once J1 lands.

**Tests:** 51 EditMode tests (Test Runner → EditMode → `QAXR.Prep.Tests`), plus a `--selftest` for each Python tool.

## Contents

| File | Roadmap | What it is |
|---|---|---|
| `Docs~/AgentSpec.md` | W1 | Agent inputs and outputs, and the `qaxr_trace_v1` CSV format |
| `Docs~/ValidationProtocol.md` | W5 | Validation procedure, stop rule, data handling and consent text |
| `Runtime/MotionMath.cs` | §4.3, W3 | Angular velocity from quaternions, artificial/physical split, yaw/pitch/roll, `EpisodeDetector`, Hampel filter, moving average |
| `Runtime/Trace.cs` | W1, W3, J6 | `TraceWriter` (30 Hz, with no catch-up burst after a hitch), `Trace.Load`/`Evaluate` for replay, `LocoCommand`, `QaxrPaths` |
| `Runtime/Risk.cs` | W2, W4, §4.4 | §4.3 rule table, exceedance → severity, `Issue` (fields from §4.6), `IssueSink`, `IssueClusterer` (0.5 m / 2 s), `ZoneRiskGrid` (1 m), `RollingAverage` |
| `Runtime/BugOracles.cs` | Z6, T3, §4.5 | Text/UI angular size, `CanReach`, `LandedInGeometry`, `HeldPenetration`, `StuckDetector`, plus `QaxrOracleMonitor` (runtime exceptions from any thread, fall-through below killY) |
| `Runtime/Zones.cs` | T2, T4, §5 | `QaxrOrigin` and the `QaxrZone` base, which acts on every origin inside. Zone 1 `FastCorridorZone`, zone 2 `RockingBoatZone`, zone 3 `CutsceneZone`, zone 4 `HeavyRoomZone`, each with a `fixed` toggle |
| `Runtime/LiveTunerPanel.cs` | L1, §4.7 | Zone 1 speed −/+, the fixed toggle, and "before → now" risk. Set `Score` from W4 |
| `Runtime/SicknessRatingPanel.cs` | W6 | In-VR 0–10 rating every 30 s, written to CSV |
| `Runtime/AgentMath.cs` | Z4, §4.2 | `Intent`, `TransitionTable`, `IntentChain` (seed + chaos), min-jerk, Fitts, lognormal, softmax target pick |
| `Runtime/QaxrUi.cs` | | Shared poke/ray keys and TMP labels for the panels and builders |
| `Runtime/BuildInfo.cs` + `Editor/CommitStamp.cs` | §4.6 | `BuildInfo.Commit`: git in the Editor, a stamped Resources file on the Quest |
| `Runtime/QaxrBreakingPoint.cs`, `ThrowingButton.cs` | §4.5 | `Oracle` enum, ground-truth markers on planted bugs, and the throwing button |
| `Editor/SicknessZonesBuilder.cs` | §5 | Menu **QAXR → Prep → Build Zones 1–4 (sickness)** |
| `Editor/BreakingPointsBuilder.cs` | §5 | Menu **QAXR → Prep → Build Zones 5 + 6 (breaking points)** |
| `Tools~/ssq_score.py` | W6 | SSQ + VRSQ from one CSV |
| `Tools~/analyze_validation.py` | W6 | Rating and risk curves, SSQ change from baseline, and whether the risk score orders bad vs fixed the way people do |
| `Tools~/agent_likeness.py` | §4.8 | Agent vs human head-speed distributions (KS, overlap) and control volumes (dwell per zone, visit order) |

## Scene layout

Both builders arrange the zones around the 10 × 10 m floor at the origin, like the one in `Empty.unity`:

- Zone 1 at +x (x 5..35)
- Zone 2 at −x (x −13..−5)
- Zone 3 at −z (z −11..−5)
- Zone 4 at −x/−z
- Zones 5–6 at +z (z 5..23)

After building, bake the NavMesh with **Render Meshes** as geometry. Otherwise the invisible doorway blocker in zone 5 is baked out and the stuck case stops working.

## Contracts the others plug into

- **Agents.** Put a `QaxrOrigin` on each agent root, set `head`, and write `loco` every frame. Ghost agents use `zoneMoveSpeed` when it is ≥ 0. Zones find the player rig themselves.
- **Issues.** Everything reports through `IssueSink.Report(Issue)`. J3's `IssueLog` subscribes to `IssueSink.Reported`, or replaces it on merge.
- **Exceptions.** `QaxrOracleMonitor.context` should return the agent that is acting right now, so exception issues carry the agent id and intent.
- **Risk timeline for W6.** Write `<participant>_<condition>_risk.csv` with the columns `t_s,score`, where `score` is `RollingAverage.Value`. `analyze_validation.py --risk` reads it.

## Merge notes

- **Assemblies.** Move `Runtime/*` into `QAXR.Runtime`, `Editor/*` into `QAXR.Editor` and `Tests/*` into the plugin's test asmdef, then delete the three `QAXR.Prep*` asmdefs.
- **Duplicates.** If J1 also defines `Intent`, `Oracle` or `Issue`, keep one. These match the frozen 11 intents, the 8 oracles of §4.5 and the issue JSON of §4.6. This `Issue` has one extra field, `detail`.
- **Hard-coded paths.** `CommitStamp.StampPath` and `BreakingPointsBuilder.Mat` point at this folder. Update them when it moves.
- **Interaction-failed oracle.** It is not written yet. It needs the agent's grab attempt: "select attempted, no `selectEntered` within t".
