using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Task-oriented, context-aware stochastic simulated VR user (heuristic, NOT a validated model of human cognition).
///
///   Perception (QAXRPerception)            what the agent can see / is near
///   Layer A  Task planning (QAXRTaskPlanner) mission -> current subgoal, completion conditions, timeouts
///   Layer B  Behaviour (QAXRBehaviorSelector) task rules + utility-based stochastic action choice
///   Layer C  Motion (QAXRMotionController)   smooth body / head execution with rate limits
///
/// Transform hierarchy (keeps D2 motion sources separate):
///   AgentRoot (position) -> Body (yaw) -> HeadPivot (simulated head yaw/pitch) -> ApplicationCameraOffset (app roll) -> AgentCamera
/// The agent writes only AgentRoot, Body and HeadPivot. The scenario writes ApplicationCameraOffset.
///
/// Determinism: stepped with a fixed dt by the scenario; uses only its own System.Random(seed).
/// No access to fault or risk information.
/// </summary>
public class QAXRAgentController : MonoBehaviour
{
    [Header("Identity")]
    public string agentId = "A01";

    [Header("Behaviour profile")]
    public QAXRProfileType profileType = QAXRProfileType.Focused;
    [Tooltip("Used when profileType = Custom.")]
    public QAXRBehaviorProfile customProfile = new QAXRBehaviorProfile { profileName = "Custom" };
    public int randomSeed = 42;
    [Tooltip("Seconds to wait at the start position before starting the mission (multi-agent staggering).")]
    public float startDelay = 0f;

    [Header("Body")]
    public float height = 1.6f;

    [Header("Mission (Layer A task definition)")]
    public string missionName = "Enter corridor -> Zone A -> cross Zone B -> find panel -> approach -> press button -> exit";
    public List<QAXRSubgoal> mission = new List<QAXRSubgoal>();

    [Header("Perception")]
    public QAXRPerception perception = new QAXRPerception();

    [Header("Motion (Layer C)")]
    public QAXRMotionController motion = new QAXRMotionController();

    [Header("Navigation area")]
    public float corridorHalfWidth = 2.1f;
    public float minZ = -1.3f;
    public float maxZ = 23.3f;
    public float avoidanceClearance = 0.3f;
    public float avoidLookahead = 4f;

    [Header("Robustness")]
    [Tooltip("Seconds without progress toward the goal (while trying to move) before a stuck recovery.")]
    public float stuckWindow = 5f;
    public int maxStuckRecoveries = 2;

    [Header("Hierarchy")]
    public Transform body;
    public Transform headPivot;
    public Transform appCameraOffset;
    public Camera agentCamera;
    public Renderer[] bodyRenderers;
    public QAXRPerceivable selfPerceivable;

    // ------------------------------------------------------------------ runtime state
    public QAXRBehaviorProfile Profile { get; private set; }
    public QAXRTaskPlanner Planner { get; } = new QAXRTaskPlanner();
    public QAXRAgentAction Action { get; private set; }
    public bool Active { get; private set; }          // present in the scene (not exited)
    public bool Started => time >= startDelay;
    public bool MissionComplete => Planner.Completed;
    public bool MissionFailed => Planner.Failed;
    public bool Done => (MissionComplete && motion.speed < 0.01f) || MissionFailed;
    public string FailureReason => Planner.FailureReason;
    public bool ButtonPressed { get; private set; }
    public float CurrentSpeed => motion.speed;
    public float BodyYawRate => motion.bodyYawRate;
    public float HeadYawRate => motion.headYawRate;
    public float HeadPitchRate => motion.headPitchRate;
    public float HeadYawRel => motion.headYaw;
    public float HeadPitchRel => motion.headPitch;
    public string AttentionName => attention != null ? attention.displayName : attentionLabel;
    public float NearestObstacle { get; private set; } = float.PositiveInfinity;
    public int DecisionCount { get; private set; }
    public ulong DecisionHash { get; private set; }
    public float TotalStopSeconds { get; private set; }
    public Vector3 SteerPoint { get; private set; }
    public Vector3 NavGoal { get; private set; }
    public string CurrentTask => Planner.MissionName;
    public string SubgoalLabel => Planner.CurrentLabel;
    public string LastReason { get; private set; } = "";
    public readonly int[] actionCounts = new int[Enum.GetValues(typeof(QAXRAgentAction)).Length];
    public readonly List<string> recentActions = new List<string>();

    public event Action<string, string> OnEvent;

    private System.Random rng;
    private float time;
    private float actionTimer, actionDuration;
    private QAXRAgentAction previousAction = QAXRAgentAction.Walk;
    private float lastNonWalkTime = -999f;
    private QAXRPerceivable focus;
    private QAXRPerceivable attention;
    private string attentionLabel = "path ahead";
    private float speedScale = 1f;
    private float detourOffset;
    private float attentionDwell;
    private bool interactionDone;
    private bool decisionPending;
    private string pendingTrigger = "start";
    private bool lastNovel;
    private readonly HashSet<QAXRPerceivable> announcedObstacles = new HashSet<QAXRPerceivable>();
    private readonly Dictionary<QAXRPerceivable, int> avoidSide = new Dictionary<QAXRPerceivable, int>();

    // glance sequence (LookAround)
    private int glancesLeft;
    private bool glanceHolding;
    private float glanceTimer, glanceYaw, glancePitch;

    // interaction sequence
    private enum InteractPhase { Align, Hesitate, Press, Observe, Done }
    private InteractPhase iPhase;
    private float iTimer, iHesitate, iObserve;

    // stuck detection
    private float bestDist = float.MaxValue, noProgress;
    private int stuckCount;
    private int lastSubgoalIndex = -1;

    // ------------------------------------------------------------------ API

    public void ResetAgent(int seed, Vector3 startPosition, float startYaw)
    {
        randomSeed = seed;
        rng = new System.Random(seed);
        Profile = profileType == QAXRProfileType.Custom ? customProfile.Clone() : QAXRBehaviorProfile.Create(profileType);
        if (body == null) body = transform;

        time = 0f;
        motion.Reset(startPosition, startYaw);
        perception.RefreshWorld();
        perception.ResetMemory();
        if (selfPerceivable != null) selfPerceivable.Owner = this;

        Action = QAXRAgentAction.Stop;
        previousAction = QAXRAgentAction.Walk;
        Active = true;
        ButtonPressed = interactionDone = false;
        DecisionCount = 0;
        DecisionHash = 1469598103934665603UL;
        TotalStopSeconds = 0f;
        lastNonWalkTime = -999f;
        focus = attention = null;
        attentionLabel = "path ahead";
        speedScale = 1f;
        detourOffset = 0f;
        attentionDwell = 0f;
        decisionPending = false;
        lastNovel = false;
        announcedObstacles.Clear();
        announcedTargets.Clear();
        avoidSide.Clear();
        bestDist = float.MaxValue; noProgress = 0f; stuckCount = 0; lastSubgoalIndex = -1;
        NearestObstacle = float.PositiveInfinity;
        Array.Clear(actionCounts, 0, actionCounts.Length);
        recentActions.Clear();
        LastReason = "";
        SetBodyVisible(true);
        if (headPivot != null) headPivot.localPosition = new Vector3(0f, Mathf.Max(0.5f, height - 0.1f), 0f);

        Planner.OnEvent -= Emit;
        Planner.OnEvent += Emit;
        Emit("AgentConfig", "profile=" + Profile.profileName + " seed=" + seed + " speed=" + F(Profile.preferredSpeed) +
                            " turn=" + F(Profile.turningSpeed) + " head=" + F(Profile.headMaxSpeed) + " height=" + F(height) +
                            " startDelay=" + F(startDelay));
        Planner.Begin(missionName, mission);
        ApplyTransforms();
        if (Planner.Failed) { Action = QAXRAgentAction.Failed; Active = false; }
        else RequestDecision("mission start");
    }

    public void Step(float dt)
    {
        if (rng == null) ResetAgent(randomSeed, transform.position, 0f);
        time += dt;

        if (!Active) return;

        if (Planner.Failed)
        {
            if (Action != QAXRAgentAction.Failed) EnterAction(QAXRAgentAction.Failed, 0f, null, Planner.FailureReason);
            MoveWith(0f, false, 0f, 0f, 0f, dt);
            return;
        }

        if (!Started)
        {
            MoveWith(0f, false, 0f, 0f, 0f, dt); // wait at the entrance
            return;
        }

        perception.Sense(this, body, headPivot);
        NearestObstacle = perception.NearestObstacleSurface();

        if (Planner.Completed)
        {
            if (Action != QAXRAgentAction.Finished) EnterAction(QAXRAgentAction.Finished, 0f, null, "mission complete");
            attention = null; attentionLabel = "path ahead";
            MoveWith(0f, false, 0f, 0f, 0f, dt);
            if (motion.speed < 0.01f)
            {
                Active = false;          // walks out through the exit: no longer an obstacle for others
                SetBodyVisible(false);
                Emit("AgentExited", "");
            }
            return;
        }

        // ---- Layer A: task planning ----
        QAXRSubgoal g = Planner.Current;
        NavGoal = Planner.NavigationPoint();
        bool targetVisible = g.target != null && perception.IsVisible(g.target);
        if (g.target != null && attention == g.target && HeadOnTarget(g.target, 10f)) attentionDwell += dt; else attentionDwell = 0f;

        var pc = new QAXRTaskPlanner.Context
        {
            position = motion.position,
            speed = motion.speed,
            facingError = g.target != null ? Mathf.Abs(Mathf.DeltaAngle(motion.bodyYaw, motion.YawTo(g.target.AttentionPosition))) : 0f,
            targetVisible = targetVisible,
            attentionDwell = attentionDwell,
            interactionDone = interactionDone
        };
        bool changed = Planner.Tick(dt, pc);
        if (Planner.Failed || Planner.Completed)
        {
            if (Planner.Completed) Emit("MissionComplete", "button_pressed=" + ButtonPressed + " decisions=" + DecisionCount);
            return; // handled next step
        }
        if (changed)
        {
            interactionDone = false;
            attentionDwell = 0f;
            RequestDecision("subgoal " + Planner.CurrentType);
        }
        g = Planner.Current;
        NavGoal = Planner.NavigationPoint();

        // ---- Event triggers for decisions ----
        bool novel = perception.AnyNovelNearby(5f);
        if (novel && !lastNovel && IsLocomotion(Action)) RequestDecision("new object noticed");
        lastNovel = novel;
        foreach (var v in perception.visible)
        {
            if ((v.obj.kind == QAXRPerceivableKind.Obstacle || v.obj.kind == QAXRPerceivableKind.Agent) &&
                v.distance - v.obj.radius < 2.5f && !announcedObstacles.Contains(v.obj))
            {
                announcedObstacles.Add(v.obj);
                if (IsLocomotion(Action)) RequestDecision("obstacle ahead: " + v.obj.displayName);
            }
        }
        if (g.type == QAXRSubgoalType.ObserveTarget && targetVisible && Action != QAXRAgentAction.LookAtTarget &&
            Action != QAXRAgentAction.Stop && !decisionPending)
            RequestDecision("task target visible");

        actionTimer += dt;
        if (Action != QAXRAgentAction.Interact && Action != QAXRAgentAction.LookAround && actionTimer >= actionDuration)
            RequestDecision(Action + " finished");
        if (decisionPending) Decide(g, targetVisible);

        // ---- Layer B -> C: execute the current action ----
        ExecuteAction(g, dt);
        DetectStuck(g, dt);
    }

    public void SetBodyVisible(bool visible)
    {
        if (bodyRenderers == null) return;
        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = visible;
    }

    // ------------------------------------------------------------------ Layer B

    private void RequestDecision(string trigger)
    {
        if (decisionPending) return;
        decisionPending = true;
        pendingTrigger = trigger;
    }

    private void Decide(QAXRSubgoal g, bool targetVisible)
    {
        decisionPending = false;
        float dist = Flat(NavGoal - motion.position).magnitude;
        bool hasLandmark = perception.TryGetUninspectedLandmark(5f, out var landmark);
        float steerErr = Mathf.Abs(Mathf.DeltaAngle(motion.bodyYaw, motion.YawTo(SteerPoint)));
        bool nearWall = Mathf.Abs(motion.position.x) > corridorHalfWidth - 0.9f;
        QAXRControlPanel targetPanel = g.target != null ? g.target.GetComponent<QAXRControlPanel>() : null;
        bool occupied = targetPanel != null && targetPanel.Occupant != null && targetPanel.Occupant != this;

        var c = new QAXRBehaviorSelector.Context
        {
            subgoal = g.type,
            distToGoal = dist,
            approaching = (g.type == QAXRSubgoalType.ObserveTarget || g.type == QAXRSubgoalType.ApproachTarget) &&
                          g.target != null && Flat(g.target.FloorPosition - motion.position).magnitude < Profile.slowdownDistance + 1.5f,
            taskTarget = g.target,
            taskTargetVisible = targetVisible,
            taskTargetNovel = g.target != null && targetVisible && Action != QAXRAgentAction.Stop && !wasTargetAnnounced(g.target),
            hasLandmark = hasLandmark,
            landmark = landmark,
            obstacleSurface = NearestObstacle,
            novelNearby = perception.AnyNovelNearby(4f),
            headingError = steerErr,
            speed = motion.speed,
            current = Action,
            previous = previousAction,
            timeSinceNonWalk = time - lastNonWalkTime,
            totalStop = TotalStopSeconds,
            openSpace = NearestObstacle > 2.5f && !nearWall,
            targetOccupied = occupied,
            trigger = pendingTrigger
        };
        if (c.taskTargetNovel) announcedTargets.Add(g.target);

        var ch = QAXRBehaviorSelector.Select(c, Profile, rng);
        speedScale = ch.speedScale;
        DecisionCount++;
        string detail = "#" + DecisionCount + " " + ch.action + " dur=" + F(ch.duration) + " speedScale=" + ch.speedScale.ToString("F3") +
                        " draw=" + (ch.draw >= 0 ? ch.draw.ToString("F4") : "-") + " focus=" + (ch.focus != null ? ch.focus.displayName : "-") +
                        " reason=" + ch.reason + (ch.utilities.Length > 0 ? " p=[" + ch.utilities + "]" : "");
        DecisionHash = Hash(DecisionHash, ch.action + "|" + F(ch.duration) + "|" + ch.speedScale.ToString("F4") + "|" + (ch.focus != null ? ch.focus.name : "-") + "|" + F(time));
        Emit("Decision", detail);
        LastReason = ch.reason;
        EnterAction(ch.action, ch.duration, ch.focus, ch.reason, ch.detourOffset);
    }

    private readonly HashSet<QAXRPerceivable> announcedTargets = new HashSet<QAXRPerceivable>();
    private bool wasTargetAnnounced(QAXRPerceivable t) => announcedTargets.Contains(t);

    private void EnterAction(QAXRAgentAction a, float duration, QAXRPerceivable f, string reason, float detour = 0f)
    {
        if (Action != a) previousAction = Action;
        Action = a;
        actionTimer = 0f;
        actionDuration = duration;
        focus = f;
        detourOffset = a == QAXRAgentAction.TurnBody ? detour : 0f;
        actionCounts[(int)a]++;
        if (a == QAXRAgentAction.Stop || a == QAXRAgentAction.InspectObject) TotalStopSeconds += duration;
        if (a == QAXRAgentAction.InspectObject || (a == QAXRAgentAction.LookAtTarget && f != null && f.kind == QAXRPerceivableKind.Landmark))
            perception.MarkInspected(f);
        if (a != QAXRAgentAction.Walk && a != QAXRAgentAction.ResumeNavigation) lastNonWalkTime = time;

        if (a == QAXRAgentAction.LookAround)
        {
            Vector2Int gr = Profile.glancesPerLookAround;
            glancesLeft = rng.Next(gr.x, gr.y + 1);
            StartGlance();
        }
        if (a == QAXRAgentAction.Interact)
        {
            iPhase = InteractPhase.Align;
            iTimer = 0f;
            iHesitate = QAXRBehaviorSelector.Range(rng, Profile.interactionHesitation);
            iObserve = QAXRBehaviorSelector.Range(rng, Profile.resultObserveTime);
        }

        recentActions.Insert(0, "t=" + time.ToString("F1") + " " + a + (f != null ? " -> " + f.displayName : ""));
        if (recentActions.Count > 6) recentActions.RemoveAt(recentActions.Count - 1);
        Emit("AgentState", a + " | " + reason);
    }

    private void StartGlance()
    {
        glanceHolding = true;
        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
        glanceYaw = side * QAXRBehaviorSelector.Range(rng, Profile.lookAroundYaw);
        glancePitch = QAXRBehaviorSelector.Range(rng, -Profile.lookAroundPitchMax, Profile.lookAroundPitchMax);
        glanceTimer = QAXRBehaviorSelector.Range(rng, Profile.glanceHold);
    }

    // ------------------------------------------------------------------ Layer C commands

    private void ExecuteAction(QAXRSubgoal g, float dt)
    {
        SteerPoint = ComputeSteerPoint(NavGoal);
        float baseSpeed = Profile.preferredSpeed * speedScale;

        // Slow down near goals where the agent must stop (panel, exit); cautious profiles start earlier.
        bool stopGoal = g.type == QAXRSubgoalType.ApproachTarget || g.type == QAXRSubgoalType.InteractWithTarget ||
                        g.type == QAXRSubgoalType.NavigateToExit;
        float dGoal = Flat(NavGoal - motion.position).magnitude;
        if (stopGoal)
        {
            if (dGoal < Profile.slowdownDistance) baseSpeed *= 0.3f + 0.7f * dGoal / Profile.slowdownDistance;
            baseSpeed = Mathf.Min(baseSpeed, Mathf.Sqrt(2f * Profile.maxDeceleration * 0.8f * Mathf.Max(0f, dGoal - 0.03f)));
        }
        if (g.type == QAXRSubgoalType.ApproachTarget && (dGoal < 0.12f ||
            (g.target != null && dGoal < 0.3f && Flat(g.target.AttentionPosition - motion.position).magnitude <= g.target.interactionRange - 0.1f)))
            baseSpeed = 0f; // inside interaction range at the approach point: stop
        if (g.type == QAXRSubgoalType.ObserveTarget && dGoal < 0.3f) baseSpeed = 0f; // reached search point
        if (NearestObstacle < 1.5f) baseSpeed *= Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(NearestObstacle / 1.5f));

        // Default head: anticipate the steering direction, slightly downward.
        float bearing = Mathf.DeltaAngle(motion.bodyYaw, motion.YawTo(SteerPoint));
        float headYaw = Mathf.Clamp(bearing * Profile.headAnticipation, -30f, 30f);
        float headPitch = 3f;
        attention = null; attentionLabel = "path ahead";

        float speed = baseSpeed;
        bool face = false; float faceYaw = 0f;

        switch (Action)
        {
            case QAXRAgentAction.Walk:
            case QAXRAgentAction.ResumeNavigation:
                break;

            case QAXRAgentAction.SlowDown:
                speed = baseSpeed * 0.55f;
                break;

            case QAXRAgentAction.Stop:
                speed = 0f;
                if (focus != null) LookAt(focus, ref headYaw, ref headPitch);
                break;

            case QAXRAgentAction.LookAtTarget:
                if (focus != null)
                {
                    LookAt(focus, ref headYaw, ref headPitch);
                    bool taskTarget = g.target == focus;
                    if (!taskTarget && Mathf.Abs(Mathf.DeltaAngle(motion.bodyYaw, motion.YawTo(focus.AttentionPosition))) > motion.headYawLimit)
                        RequestDecision("landmark out of view");
                    if (taskTarget && g.type == QAXRSubgoalType.ApproachTarget && baseSpeed <= 0f && motion.speed < 0.3f)
                    {
                        face = true; // arrived: turn in place to align the body with the panel
                        faceYaw = motion.YawTo(focus.AttentionPosition);
                    }
                }
                break;

            case QAXRAgentAction.LookAround:
                speed = baseSpeed * 0.8f;
                glanceTimer -= dt;
                if (glanceHolding) { headYaw = glanceYaw; headPitch = glancePitch; attentionLabel = "exploratory glance"; }
                if (glanceTimer <= 0f)
                {
                    if (glanceHolding)
                    {
                        glanceHolding = false;
                        glanceTimer = QAXRBehaviorSelector.Range(rng, Profile.glancePause);
                        glancesLeft--;
                    }
                    else if (glancesLeft > 0) StartGlance();
                    else RequestDecision("look around finished");
                }
                break;

            case QAXRAgentAction.TurnBody:
                speed = baseSpeed * 0.85f;
                break;

            case QAXRAgentAction.InspectObject:
                speed = 0f;
                if (focus != null)
                {
                    LookAt(focus, ref headYaw, ref headPitch);
                    float rel = Mathf.DeltaAngle(motion.bodyYaw, motion.YawTo(focus.AttentionPosition));
                    if (Mathf.Abs(rel) > motion.headYawLimit * 0.7f && motion.speed < 0.2f)
                    {
                        face = true; // turn the body partly toward the object, head does the rest
                        faceYaw = motion.YawTo(focus.AttentionPosition) - Mathf.Sign(rel) * motion.headYawLimit * 0.5f;
                    }
                }
                break;

            case QAXRAgentAction.Interact:
                speed = 0f;
                if (g.target != null)
                {
                    LookAt(g.target, ref headYaw, ref headPitch);
                    face = true;
                    faceYaw = motion.YawTo(g.target.AttentionPosition);
                    RunInteraction(g, dt, Mathf.Abs(Mathf.DeltaAngle(motion.bodyYaw, faceYaw)));
                }
                break;
        }

        MoveWith(speed, face, faceYaw, headYaw, headPitch, dt);
    }

    private void RunInteraction(QAXRSubgoal g, float dt, float facingErr)
    {
        QAXRControlPanel panel = g.target.GetComponent<QAXRControlPanel>();
        iTimer += dt;
        switch (iPhase)
        {
            case InteractPhase.Align:
                if (panel != null) panel.Occupant = this;
                if ((facingErr < 8f && motion.speed < 0.02f && HeadOnTarget(g.target, 8f)) || iTimer > 4f)
                {
                    iPhase = InteractPhase.Hesitate; iTimer = 0f;
                    Emit("InteractionPhase", "aligned, hesitate " + F(iHesitate) + "s");
                }
                break;
            case InteractPhase.Hesitate:
                if (iTimer >= iHesitate) { iPhase = InteractPhase.Press; iTimer = 0f; }
                break;
            case InteractPhase.Press:
                float range = Flat(g.target.AttentionPosition - motion.position).magnitude;
                if (range <= g.target.interactionRange)
                {
                    ButtonPressed = true;
                    if (panel != null) panel.Press(this);
                    Emit("ButtonPressed", g.target.displayName + " range=" + F(range) + "m");
                }
                else Emit("InteractionFailed", "out of range " + F(range) + "m");
                iPhase = InteractPhase.Observe; iTimer = 0f;
                Emit("InteractionPhase", "observe result " + F(iObserve) + "s");
                break;
            case InteractPhase.Observe:
                if (iTimer >= iObserve)
                {
                    iPhase = InteractPhase.Done;
                    if (panel != null && panel.Occupant == this) panel.Occupant = null;
                    interactionDone = ButtonPressed;
                    if (!ButtonPressed) Planner.Fail("Interaction with '" + g.target.displayName + "' failed (out of range)");
                }
                break;
        }
    }

    private void MoveWith(float speed, bool face, float faceYaw, float headYaw, float headPitch, float dt)
    {
        var cmd = new QAXRMotionController.Command
        {
            steerPoint = SteerPoint == Vector3.zero && !Started ? motion.position : SteerPoint,
            targetSpeed = speed,
            hasFaceYaw = face,
            faceYaw = faceYaw,
            headYaw = headYaw,
            headPitch = headPitch
        };
        if (!Started || Planner.Failed || Planner.Completed) { cmd.steerPoint = motion.position; cmd.hasFaceYaw = false; }
        motion.Step(dt, cmd, Profile, corridorHalfWidth, minZ, maxZ, perception.visible);
        ApplyTransforms();
    }

    private void ApplyTransforms()
    {
        transform.position = motion.position;
        transform.rotation = Quaternion.identity;
        if (body != null && body != transform) body.localRotation = Quaternion.Euler(0f, motion.bodyYaw, 0f);
        else transform.rotation = Quaternion.Euler(0f, motion.bodyYaw, 0f);
        if (headPivot != null) headPivot.localRotation = Quaternion.Euler(motion.headPitch, motion.headYaw, 0f);
    }

    private void LookAt(QAXRPerceivable obj, ref float yaw, ref float pitch)
    {
        attention = obj;
        Vector3 eye = headPivot != null ? headPivot.position : motion.position + Vector3.up * (height - 0.1f);
        Vector3 d = obj.AttentionPosition - eye;
        float horiz = new Vector2(d.x, d.z).magnitude;
        yaw = Mathf.DeltaAngle(motion.bodyYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        pitch = -Mathf.Atan2(d.y, Mathf.Max(0.01f, horiz)) * Mathf.Rad2Deg;
    }

    private bool HeadOnTarget(QAXRPerceivable obj, float tolerance)
    {
        if (headPivot == null || obj == null) return false;
        Vector3 d = obj.AttentionPosition - headPivot.position;
        return Vector3.Angle(headPivot.forward, d) <= tolerance;
    }

    /// <summary>Steering point with bounded detour and local obstacle avoidance (sticky side choice).</summary>
    private Vector3 ComputeSteerPoint(Vector3 goal)
    {
        Vector3 pos = motion.position;
        Vector3 target = goal;
        Vector3 toGoal = Flat(goal - pos);
        float lim = corridorHalfWidth - motion.bodyRadius - 0.1f;

        if (Action == QAXRAgentAction.TurnBody && Mathf.Abs(detourOffset) > 0.01f && toGoal.magnitude > 1f)
        {
            Vector3 dir = toGoal.normalized;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);
            target = pos + dir * Mathf.Min(2.5f, toGoal.magnitude) + perp * detourOffset;
            target.x = Mathf.Clamp(target.x, -lim + 0.3f, lim - 0.3f);
        }

        Vector3 seg = Flat(target - pos);
        float len = seg.magnitude;
        if (len < 0.01f) return target;
        Vector3 sdir = seg / len;

        QAXRPerceivable blocker = null;
        float bestT = float.MaxValue, blockerR = 0f;
        foreach (var v in perception.visible)
        {
            if (v.obj.kind != QAXRPerceivableKind.Obstacle && v.obj.kind != QAXRPerceivableKind.Agent) continue;
            Vector3 c = v.obj.FloorPosition;
            float R = v.obj.radius + motion.bodyRadius + avoidanceClearance;
            float t = Vector3.Dot(c - pos, sdir);
            // Keep avoiding while the obstacle is still alongside (until the agent passes the side point).
            if (t <= -R * 0.6f || t > len + R || Flat(c - pos).magnitude > avoidLookahead) continue;
            Vector3 closest = pos + sdir * Mathf.Clamp(t, 0f, len);
            if (Flat(c - closest).magnitude >= R) continue;
            if (t < bestT) { bestT = t; blocker = v.obj; blockerR = R; }
        }
        if (blocker == null) return target;

        Vector3 bc = blocker.FloorPosition;
        Vector3 perpL = new Vector3(-sdir.z, 0f, sdir.x);
        if (!avoidSide.TryGetValue(blocker, out int side))
        {
            Vector3 left = bc + perpL * (blockerR + 0.1f);
            Vector3 right = bc - perpL * (blockerR + 0.1f);
            bool leftOk = Mathf.Abs(left.x) <= lim, rightOk = Mathf.Abs(right.x) <= lim;
            float cross = Vector3.Cross(sdir, Flat(bc - pos)).y; // >0: obstacle center right of path
            side = leftOk && rightOk ? (cross > 0f ? 1 : -1) : (leftOk ? 1 : -1);
            avoidSide[blocker] = side;
            Emit("ObstacleAvoidance", blocker.displayName + " pass on " + (side > 0 ? "left" : "right"));
        }
        // Tangent steering: head along the tangent of the clearance circle on the chosen side.
        Vector3 toC = Flat(bc - pos);
        float dc = toC.magnitude;
        Vector3 cdir = dc > 1e-4f ? toC / dc : sdir;
        Vector3 steerDir;
        if (dc > blockerR + 0.01f)
        {
            float ang = Mathf.Asin(Mathf.Clamp01(blockerR / dc)) * Mathf.Rad2Deg;
            steerDir = Quaternion.Euler(0f, -side * ang, 0f) * cdir;
        }
        else
        {
            Vector3 tangent = Quaternion.Euler(0f, -side * 90f, 0f) * cdir;
            steerDir = (tangent - cdir * 0.5f).normalized; // inside the clearance zone: move around and outward
        }
        Vector3 p = pos + steerDir * Mathf.Max(dc, 1.0f);
        p.x = Mathf.Clamp(p.x, -lim, lim);
        p.y = 0f;
        return p;
    }

    private void DetectStuck(QAXRSubgoal g, float dt)
    {
        if (Planner.Index != lastSubgoalIndex) { lastSubgoalIndex = Planner.Index; bestDist = float.MaxValue; noProgress = 0f; }
        bool tryingToMove = IsLocomotion(Action) && Profile.preferredSpeed * speedScale > 0.1f &&
                            !(g.type == QAXRSubgoalType.ObserveTarget && Flat(NavGoal - motion.position).magnitude < 0.3f) &&
                            !(g.type == QAXRSubgoalType.ApproachTarget && Flat(NavGoal - motion.position).magnitude < 0.2f);
        if (!tryingToMove) return;
        float d = Flat(NavGoal - motion.position).magnitude;
        if (d < bestDist - 0.15f) { bestDist = d; noProgress = 0f; return; }
        noProgress += dt;
        if (noProgress < stuckWindow) return;

        stuckCount++;
        noProgress = 0f;
        bestDist = d;
        Emit("StuckDetected", "no progress toward '" + g.name + "' for " + F(stuckWindow) + "s (recovery " + stuckCount + ")");
        var keys = new List<QAXRPerceivable>(avoidSide.Keys);
        foreach (var k in keys) avoidSide[k] = -avoidSide[k]; // try the other side
        if (stuckCount > maxStuckRecoveries)
            Planner.Fail("Agent stuck: '" + g.name + "' unreachable (no progress after " + maxStuckRecoveries + " recoveries)");
    }

    private static bool IsLocomotion(QAXRAgentAction a) =>
        a == QAXRAgentAction.Walk || a == QAXRAgentAction.SlowDown || a == QAXRAgentAction.ResumeNavigation ||
        a == QAXRAgentAction.LookAtTarget || a == QAXRAgentAction.LookAround || a == QAXRAgentAction.TurnBody;

    private void Emit(string type, string detail) => OnEvent?.Invoke(type, detail);

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    private static string F(float v) => v.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static ulong Hash(ulong h, string s)
    {
        unchecked
        {
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; }
            h ^= 0xFF; h *= 1099511628211UL;
        }
        return h;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(motion.position + Vector3.up * 0.1f, SteerPoint + Vector3.up * 0.1f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(NavGoal, 0.2f);
    }
}
