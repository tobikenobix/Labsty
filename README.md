# XR Hackathon Starter: PRISM, the XR Choice Lab

Unity project for *Extended Reality for Value Creation in Businesses* (TUM School of Management and ETH Zürich, autumn 2026). Session 4 on 16 October and the hackathon in Bad Tölz (19 to 22 November) build on it.

The Playground scene is a complete VR showroom for Meta Quest 3. A visitor picks a shopping mission, wakes a fictional speaker (ECHO ONE) by docking its energy core, dresses it in fabric finishes and light moods, meets it in three living spaces that react to their presence, rates it, chooses between two price points, and finally reads a summary of their own session linked to consumer-behaviour ideas. Every interaction is written to a CSV, so the scene doubles as a small research instrument.

**Unity version: 6000.3.25f1 (Unity 6 LTS).** Everyone in a team uses this one version. A second version corrupts the project.

## The experience

| Step | Where | What the visitor does | Logged as |
|---|---|---|---|
| 0 | Welcome card and mission stand | Presses *Working from home*, *Hosting friends* or *Relaxing after work*. The matching space gets a START HERE marker | `mission_chosen` |
| 1 | Energy core plinth | Grabs the glowing core and docks it in the ring on the front of ECHO ONE | `step_done insert_core` |
| 2 | Configurator lectern | Touches one of six fabric swatches | `variant_set` |
| 3 | Same lectern | Picks one of five light moods; a tinted light glows around the product | `variant_set` |
| 4 | FOCUS, SOCIAL and UNWIND spaces | Steps onto a pad; the space's ECHO ONE wakes up with its own light and soundtrack (SOCIAL pulses to the beat) | `bay_entered`, `bay_left` |
| 5 | Evaluation studio | Three 1 to 7 ratings, then ECHO ONE €249, ECHO LITE €149 or neither, with a comparison card | `rating`, `purchase_decision` |
| Reveal | Insight wall | Two columns: the session's data (mission, finish, light, message, time per space, ratings, choice) and what it can suggest, worded as possibilities, never findings | |

Each session is randomly assigned one of two marketing framings for the product banner (`MarketingCondition`, instructors can force A or B).

## What is in here

| Folder | Content |
|---|---|
| `Assets/_Project/Scenes` | `Playground.unity` (the showroom) and `Empty.unity` (floor and XR rig only, start your own scene from it) |
| `Assets/_Project/Prefabs` | `EchoOne`, `EnergyCore`, `Socket`, `PokeButton`, `Panel`, `ReferenceFigure` (1.8 m, for scale checks) |
| `Assets/_Project/Scripts` | The teaching scripts (below) plus the PRISM stations |
| `Assets/_Project/Steps` | One `ScenarioStep` asset per step of the Playground scenario |
| `Assets/_Project/Materials`, `Models`, `Audio`, `UI` | Generated materials, procedural meshes, three original soundtracks, painted window views and the monitor screen |

| `Assets/_Team` | Your own scenes, prefabs, scripts and assets go here |

The component shelf at the left of the showroom holds one copy of every building block with the prefab path on its label: grabbable, snap socket, poke button, info panel, teleport pad. Your own scene is these blocks arranged around your story.

## Scripts

Teaching scripts, the pattern every hackathon scene uses:

| Script | Role |
|---|---|
| `ScenarioController` | Holds the list of steps and the current index. `Complete(step, object)` advances; one event, `OnStepChanged`, drives everything else |
| `ScenarioStep` | A step as data: id, instruction, object to highlight, expected object |
| `PlaceStep`, `PressStep` | Put on a socket or a button; they complete their step when used |
| `ShowPanel`, `Highlight`, `StepLogger` | Listen to the controller: show the instruction, light up the target, write the CSV |
| `ChangeVariant`, `HoverFeedback`, `SceneSwitcher` | Material variants, hover and select feedback, scene loading |

PRISM stations built from the same pattern:

| Script | Role |
|---|---|
| `MissionSelector` | The shopping mission, with a marker on the matching space |
| `BayZone` | A space that wakes its product when the head enters: light, soundtrack, dwell time, optional audio-reactive glow |
| `MarketingCondition` | A or B framing per session, logged |
| `EvaluationStation` | Ratings and the three-way decision |
| `InsightPanel` | The session summary, drawn with rich text bars |

Logging: one CSV per session at `Application.persistentDataPath/logs`, on the Quest under `Android/data/de.tum.xrhackathon.starter/files/logs`. Columns: timestamp, time since start, event, step, object, head position and yaw, session, participant, condition. Pull the files with Meta Quest Developer Hub (File Manager) or `adb pull`.

## ChemLab score files

Every finished lab run writes two files named after the session, `session_yyyyMMdd_HHmmss.json` and `.csv`, to `Application.persistentDataPath/ChemLabSessions`. On the Quest that is `/sdcard/Android/data/de.tum.xrhackathon.starter/files/ChemLabSessions`. A run that is left before the end screen writes nothing. If writing fails, the end screen shows no "Exported" line and the error is in the log.

Pull all runs with the headset connected over USB:

```
adb pull /sdcard/Android/data/de.tum.xrhackathon.starter/files/ChemLabSessions ./sessions
```

Meta Quest Developer Hub (File Manager) works too. The folder changes if the package name in Player Settings changes.

JSON, one object per run:

| Field | Meaning |
|---|---|
| `sessionId`, `studentId`, `attempt` | Run identity. `studentId` is currently always `student_001`, `attempt` restarts at 0 with the app |
| `language`, `mode` | `EN`/`DE`/`FR`/`IT`; `mode` is always `assessment` |
| `completion_time`, `timed_out` | Seconds from lab start to the end; true when the timer ran out |
| `hazardsInRun`, `hazards_found`, `hazards_missed` | How many hazards were in this run, and the ids found and missed |
| `wrong_answers`, `falsePositiveClicks` | All wrong submissions; clicks on objects that are not hazards (not scored) |
| `score`, `detectionPoints`, `decisionPoints` | Points per hazard found plus points per first answer correct (weights in the lab config); `score` is the percentage of the points this run offered |
| `outcome` | `Pass`, `RetrainingRequired` or `SafetyReviewRequired` |
| `minimumScoreAchieved` … `safetyReviewRequired` | The pass conditions behind the outcome |
| `hazards[]` | One entry per hazard: `id`, `questionId`, `category`, `status`, `mandatoryCritical`, `detected`, `detectedAt`, `selectedOption` (first answer), `correctOption`, `optionOrder` (the keys in the shuffled order the student saw them; rows are lettered A-D by position, the option fields keep the keys from hazards.json), `isCorrect`, `responseTimeSeconds`, `wrongAnswers`, `resolved`, `criticalError` |

CSV: one row per hazard with the columns `session_id, student_id, hazard_id, question_id, category, detected, detected_at, first_answer, correct_answer, first_attempt_correct, wrong_answers, resolved, critical_error, response_time`. After an empty line follows a one-row summary: `session_id, hazards_found, hazards_missed, wrong_answers, completion_time, score, outcome`. Times are seconds with one decimal and a dot.

## First build in ten steps

1. Install Unity Hub, sign in, install the Unity version above with Android Build Support, OpenJDK, Android SDK and NDK Tools.
2. Press *Use this template* on GitHub, create one repository per team, invite your teammates.
3. Clone it with GitHub Desktop into a folder without spaces or special characters.
4. Unity Hub, Projects, Add, choose the folder, open. The first import takes 5 to 15 minutes.
5. Open `Assets/_Project/Scenes/Playground.unity`. Press Play once, press Stop. The Console must be free of red lines.
6. Put the Quest 3 into developer mode (Meta Horizon phone app, Devices, Headset settings, Developer mode). Install Meta Quest Developer Hub and sign in.
7. Plug in the headset, put it on, allow USB debugging, tick *Always allow from this computer*. Developer Hub shows the device.
8. File, Build Profiles, select Android, Switch Platform if asked. Edit, Project Settings, XR Plug-in Management, Project Validation: everything green, otherwise Fix All.
9. Build And Run. The first build takes 3 to 8 minutes. The headset launches the Playground.
10. In the headset: press a mission button, dock the core, try the swatches, walk the three spaces, rate and decide, read your summary. Unplug the cable, it still runs.

Packages already installed and configured: XR Plug-in Management with OpenXR and the Meta Quest feature group, XR Interaction Toolkit 3.6 with the Starter Assets and Hands Interaction Demo samples, XR Hands 1.9 with the hand visualizer, Input System, TextMeshPro, Unity OpenXR Meta and glTFast. The rig is the XRI *XR Origin Hands* rig: controllers and tracked hands both work, with one ray for pointing, pressing and teleporting.

## Team rules

* One Unity version. Pull before you start, push before you stop. Commit after every working step with a message that says what works now.
* One person per scene at a time. Put your work into prefabs in `Assets/_Team`; prefabs merge, scenes do not.
* Plain git is enough: no Git LFS. Keep models under 50 000 triangles and every file under 100 MB, GitHub's limit.
* Keep an AI log (what you asked, what came back, what you kept) and a credits list for every asset that is not yours. Both go into the pitch and the individual report.

## Help

Scope, concept, interactions and AI workflow: Mohamed Mansuri, mohamed.mansuri@tum.de. Setup, builds, devices and Git: Ehbal Ablimit, ehbal.ablimit@tum.de. Organisation: Justin Eymann, eymannj@ethz.ch. Bugs in the starter: Ehbal.
