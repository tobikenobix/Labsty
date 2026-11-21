# Labsty: lab safety inspection in VR

Unity project of team *404 Morning Not Found* for *Extended Reality for Value Creation in Businesses* (TUM School of Management and ETH Zürich, autumn 2026), built at the hackathon in Bad Tölz (19 to 22 November) on the course's XR starter.

Labsty is a VR safety assessment for Meta Quest 3. A student enters their ID in a briefing room, practises the controls in a small practice room, then has seven minutes to find the hazards hidden in a chemistry lab and decide how to fix each one. The result is a report card with a pass, retraining or safety-review outcome, and every run is exported as JSON and CSV.

**Unity version: 6000.3.25f1 (Unity 6 LTS).** Everyone in the team uses this one version. A second version corrupts the project.

## The experience

| Scene | What the student does |
|---|---|
| `Lobby` | Picks language (EN, DE, FR, IT), dominant hand and, optionally, which hazards are in play. *Start Lab* asks for the student ID on a keypad. After a run the report card takes the start panel's place, with a leaderboard beside it |
| `Tutorial` | Seven practice steps in a fixed order: teleport, turn, inspect a safe object, answer a hazard question, pick a dangerous answer on purpose, toggle the task panel, press *Finish*. *Skip to the lab* works at any time. Nothing is scored |
| `Lab` | A 7-minute countdown. Point at an object and press the trigger to inspect it. A hazard opens a 4-option question (shuffled); a correct answer resolves it with a green check, a wrong one gives a red cross and one line of feedback. The run ends when every hazard is resolved, the timer runs out, or the student presses *Finish* on the task panel |

The ten hazards, with their questions, answers and explanations, live in `Assets/_Project/Resources/ChemLab/hazards.json`. Four are safety-critical (blocked emergency exit, unlabelled bottle, damaged power cable, improper chemical storage); these are always in a run. Each hazard has a pool of questions, and hazards with `HazardZone`s land at a random spot inside their own area each run.

### Scoring

3 points per hazard found, 7 per first answer correct, minus 1 per false click after the third; the score is the percentage of the points the run offered.

| Outcome | When |
|---|---|
| `SafetyReviewRequired` | A dangerous (critical) answer was chosen, or a safety-critical hazard was not answered correctly |
| `RetrainingRequired` | Score below 80, or fewer than 8 of 10 hazards found or answered correctly (scaled to the hazards in the run) |
| `Pass` | Everything else |

Weights, pass marks and the time limit are at the top of `hazards.json`.

### Controls

| Quest 3 | Keyboard and mouse (Play in the Editor without a headset) | Action |
|---|---|---|
| Thumbstick forward, release | Click a teleport ring, or WASD / arrows | Teleport |
| Thumbstick left / right / back | Right mouse drag | Snap turn 45°, turn around |
| Trigger | Left click | Inspect, press UI buttons |
| A | Enter | Confirm, submit answer |
| B | Escape, Backspace | Close panel |
| X | H | Show / hide the task panel |
| Y | | Show / hide the controller button hints |

Left-handed mode (lobby) swaps A/B with X/Y and moves the task panel to the right controller.

Testing aids: *Developer mode* in the lobby's hazard plate highlights every hazard in the lab and shows its placement zones (Z in the Editor); the run is not exported and the switch resets on app restart. C tints all colliders in Editor and development builds.

## What is in here

| Folder | Content |
|---|---|
| `Assets/_Project/Scenes` | `Lobby`, `Tutorial`, `Lab` (in the build, in that order); `Playground` and `Empty` from the starter (not in the build) |
| `Assets/_Project/Scripts` | `ChemLab*` scripts for the three scenes, `HazardZone` / `HazardPlacement` / `ZoneView` for random placement, `L10n` for translations; the starter's teaching scripts |
| `Assets/_Project/Resources/ChemLab` | `hazards.json`, the sounds (a missing clip falls back to a generated tone), the outline shader, the logo |
| `Assets/_Project/Localization` | Unity Localization tables `UI` and `Hazards` in four languages (Window, Asset Management, Localization Tables) |
| `Assets/_Project/LabEnvironment` | The lab room model, its materials and textures |
| `Assets/_Project/Editor` | Scene and environment builders, static mesh merge for the lab, hazard placement tools |

All UI is built in code by `ChemLabUIKit` as world-space sign plates in ISO 7010 style.

### Script map

| Script | Role |
|---|---|
| `ChemLabLobby` | Start panel, hazard choice, student ID keypad, report card after a run |
| `ChemLabTutorial` | The practice room's seven steps |
| `ChemLabSession` | The lab game loop, timer and scoring |
| `ChemLabRun` | Data carried between scenes; settings saved on the headset; JSON / CSV export |
| `ChemLabData` | The `hazards.json` shapes and per-run hazard records |
| `ChemLabInspectable` | One component for hazards, practice objects and decoys; marks checked, wrong and resolved objects |
| `ChemLabUI`, `ChemLabQuestionPanel`, `ChemLabEndScreen` | Task panel, question plate, report card |
| `ChemLabControls`, `ChemLabControllerLabels` | Button mapping, desktop fallback, foveated rendering, controller labels |
| `ChemLabTeleportNode`, `ChemLabSafeTeleportArea` | Teleport rings; floor that never drops the player into furniture |
| `HazardZone`, `HazardPlacement` | Boxes a hazard may land in, and the seeded random placement |

### Editing hazards

* Text, correct answers, critical flags and explanations: `hazards.json` for English, the `Hazards` table for the other languages.
* Placement zones: select a `HazardZone` in the Scene view; green dots are valid spots, red ones are blocked. *Chemistry Lab, Hazard Placement* previews a random placement (Ctrl+Z undoes it) or checks 200 seeds.
* To replay a run's placement, set `ChemLabRun.PlacementSeed` to the seed from its log.

## Score files

Every finished lab run writes three files named after the session, `session_yyyyMMdd_HHmmss.json`, `_hazards.csv` and `_summary.csv`, to `Application.persistentDataPath/ChemLabSessions`. On the Quest that is `/sdcard/Android/data/de.tum.xrhackathon.labsty/files/ChemLabSessions`. A run that is left before the end screen, or one in developer mode, writes nothing. If writing fails, the end screen shows no "Exported" line and the error is in the log.

Pull all runs with the headset connected over USB:

```
adb pull /sdcard/Android/data/de.tum.xrhackathon.labsty/files/ChemLabSessions ./sessions
```

Meta Quest Developer Hub (File Manager) works too. The folder changes if the package name in Player Settings changes.

JSON, one object per run:

| Field | Meaning |
|---|---|
| `sessionId`, `studentId`, `attempt` | Run identity. `studentId` is the number the student typed on the lobby keypad (`no_id` if the lab scene was started directly); `attempt` counts that ID's runs on this headset, starting at 1 |
| `language`, `mode` | `EN`/`DE`/`FR`/`IT`; `mode` is always `assessment` |
| `completion_time`, `timed_out` | Seconds from lab start to the end; true when the timer ran out |
| `hazardsInRun`, `hazards_found`, `hazards_missed` | How many hazards were in this run, and the ids found and missed |
| `wrong_answers`, `falsePositiveClicks`, `falseClickPenalty` | All wrong submissions; clicks on objects that are not hazards; detection points lost to them (1 per false click after the third) |
| `score`, `detectionPoints`, `decisionPoints` | Points per hazard found plus points per first answer correct (weights in the lab config); `score` is the percentage of the points this run offered |
| `outcome` | `Pass`, `RetrainingRequired` or `SafetyReviewRequired` |
| `minimumScoreAchieved` … `safetyReviewRequired` | The pass conditions behind the outcome |
| `hazards[]` | One entry per hazard: `id`, `questionId`, `category`, `status`, `mandatoryCritical`, `detected`, `detectedAt`, `selectedOption` (first answer), `correctOption`, `optionOrder` (the keys in the shuffled order the student saw them; rows are lettered A-D by position, the option fields keep the keys from hazards.json), `isCorrect`, `responseTimeSeconds`, `wrongAnswers`, `resolved`, `criticalError` |

CSV, two flat tables so the files of many runs can be concatenated (header once):

- `_hazards.csv`, one row per hazard: `session_id, student_id, attempt, hazard_id, question_id, category, detected, detected_at, first_answer, correct_answer, first_attempt_correct, wrong_answers, resolved, critical_error, response_time`
- `_summary.csv`, one row per run: `session_id, student_id, attempt, language, hazards_in_run, hazards_found, hazards_missed, wrong_answers, false_clicks, false_click_penalty, completion_time, timed_out, score, outcome`

Times are seconds with one decimal and a dot. Fields that contain a comma or quote are quoted as in RFC 4180.

## Build and run

1. Install Unity Hub and the Unity version above with Android Build Support, OpenJDK, Android SDK and NDK Tools.
2. Clone the repository into a folder without spaces or special characters, then Unity Hub, Projects, Add, choose the folder, open. The first import takes 5 to 15 minutes.
3. Open `Assets/_Project/Scenes/Lobby.unity`. Press Play: without a headset the keyboard and mouse fallback starts. The Console must be free of red lines.
4. Put the Quest 3 into developer mode (Meta Horizon phone app, Devices, Headset settings, Developer mode). Install Meta Quest Developer Hub and sign in.
5. Plug in the headset, allow USB debugging, tick *Always allow from this computer*.
6. File, Build Profiles, Android, Switch Platform if asked. Edit, Project Settings, XR Plug-in Management, Project Validation: everything green, otherwise Fix All.
7. Build And Run. The headset launches the Lobby. Unplug the cable, it still runs.

Packages: XR Plug-in Management with OpenXR and the Meta Quest feature group, XR Interaction Toolkit 3.6, XR Hands 1.9, Input System, TextMeshPro, Localization, URP, Unity OpenXR Meta and glTFast. The rig is the XRI starter rig with teleport-only locomotion.

## Team rules

* One Unity version. Pull before you start, push before you stop. Commit after every working step with a message that says what works now.
* One person per scene at a time; prefabs merge, scenes do not.
* Plain git is enough: no Git LFS. Keep models under 50 000 triangles and every file under 100 MB, GitHub's limit.
* Keep an AI log (what you asked, what came back, what you kept) and a credits list for every asset that is not yours. Both go into the pitch and the individual report.

## The starter scene

`Playground.unity` is the course starter's VR showroom (the ECHO ONE speaker with missions, configurator, three living spaces and an evaluation studio, logging to `Application.persistentDataPath/logs`). It and `Empty.unity` stay in the project for reference but are not in the build. Its scripts (`ScenarioController`, `ScenarioStep`, `PlaceStep`, `PressStep`, `ShowPanel`, `Highlight`, `StepLogger`, `MissionSelector`, `BayZone`, `MarketingCondition`, `EvaluationStation`, `InsightPanel`) are unused by Labsty.

## Help

Scope, concept, interactions and AI workflow: Mohamed Mansuri, mohamed.mansuri@tum.de. Setup, builds, devices and Git: Ehbal Ablimit, ehbal.ablimit@tum.de. Organisation: Justin Eymann, eymannj@ethz.ch.
