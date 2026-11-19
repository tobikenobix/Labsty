using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Layer C - Human-like motion execution (no skeletal animation).
/// Body: smooth yaw toward the steering point (SmoothDampAngle, hard-capped at turningSpeed),
///       speed with smoothed acceleration hard-capped at maxAcceleration / maxDeceleration,
///       automatic slowing for sharp turns, corridor bounds and obstacle footprint constraints.
/// Head: separate pivot relative to the body, smooth yaw/pitch with an angular-speed cap and joint limits.
/// The caps guarantee continuous motion: no instantaneous jumps in position, heading or head pose.
/// </summary>
[Serializable]
public class QAXRMotionController
{
    public float bodyRadius = 0.25f;
    public float speedSmoothTime = 0.3f;
    public float headYawLimit = 75f;
    public float headPitchUpLimit = 30f;
    public float headPitchDownLimit = 40f;

    public struct Command
    {
        public Vector3 steerPoint;
        public float targetSpeed;
        public bool hasFaceYaw;      // orient body to faceYaw instead of the steering direction
        public float faceYaw;
        public float headYaw;        // target, relative to body (deg, + = right)
        public float headPitch;      // target (deg, + = down)
    }

    [NonSerialized] public Vector3 position;
    [NonSerialized] public float bodyYaw, speed, headYaw, headPitch;
    [NonSerialized] public float bodyYawRate, headYawRate, headPitchRate, longitudinalAccel;
    [NonSerialized] public int constraintCorrections;

    [NonSerialized] private float bodyYawVel, speedVel, headYawVel, headPitchVel;

    public void Reset(Vector3 pos, float yaw)
    {
        position = new Vector3(pos.x, 0f, pos.z);
        bodyYaw = yaw;
        speed = headYaw = headPitch = 0f;
        bodyYawRate = headYawRate = headPitchRate = longitudinalAccel = 0f;
        bodyYawVel = speedVel = headYawVel = headPitchVel = 0f;
        constraintCorrections = 0;
    }

    public float YawTo(Vector3 point)
    {
        Vector3 d = point - position; d.y = 0f;
        return d.sqrMagnitude < 1e-6f ? bodyYaw : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    public void Step(float dt, Command cmd, QAXRBehaviorProfile p, float halfWidth, float minZ, float maxZ,
                     List<QAXRPerception.Percept> obstacles)
    {
        // ---- Body orientation ----
        Vector3 toSteer = cmd.steerPoint - position; toSteer.y = 0f;
        float desiredYaw = cmd.hasFaceYaw ? cmd.faceYaw : (toSteer.magnitude > 0.05f ? YawTo(cmd.steerPoint) : bodyYaw);
        float prevYaw = bodyYaw;
        bodyYaw = SmoothCapped(bodyYaw, desiredYaw, ref bodyYawVel, p.bodyTurnSmoothTime, p.turningSpeed, dt);
        bodyYawRate = Mathf.DeltaAngle(prevYaw, bodyYaw) / dt;

        // ---- Speed: slow for sharp turns, smoothed and capped acceleration ----
        float err = Mathf.Abs(Mathf.DeltaAngle(bodyYaw, desiredYaw));
        float turnFactor = cmd.hasFaceYaw ? 1f : 1f / (1f + (err / Mathf.Max(1f, p.turnSlowdownAngle)) * (err / Mathf.Max(1f, p.turnSlowdownAngle)));
        float target = Mathf.Max(0f, cmd.targetSpeed * turnFactor);
        float smoothed = Mathf.SmoothDamp(speed, target, ref speedVel, speedSmoothTime, Mathf.Infinity, dt);
        float ds = Mathf.Clamp(smoothed - speed, -p.maxDeceleration * dt, p.maxAcceleration * dt);
        float prevSpeed = speed;
        speed = Mathf.Max(0f, speed + ds);
        if (Mathf.Abs(smoothed - speed) > 1e-4f) speedVel = (speed - prevSpeed) / dt;
        longitudinalAccel = (speed - prevSpeed) / dt;

        // ---- Translate with constraints ----
        float rad = bodyYaw * Mathf.Deg2Rad;
        Vector3 next = position + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * (speed * dt);
        float lim = halfWidth - bodyRadius;
        Vector3 before = next;
        next.x = Mathf.Clamp(next.x, -lim, lim);
        next.z = Mathf.Clamp(next.z, minZ, maxZ);
        if (obstacles != null)
        {
            for (int i = 0; i < obstacles.Count; i++)
            {
                QAXRPerceivable o = obstacles[i].obj;
                if (o.kind != QAXRPerceivableKind.Obstacle && o.kind != QAXRPerceivableKind.Agent) continue;
                Vector3 c = o.FloorPosition;
                Vector3 d = next - c; d.y = 0f;
                float minD = o.radius + bodyRadius;
                if (d.magnitude < minD)
                {
                    next = c + (d.sqrMagnitude < 1e-6f ? Vector3.right : d.normalized) * minD;
                    next.y = 0f;
                }
            }
        }
        if ((next - before).sqrMagnitude > 1e-8f) constraintCorrections++;
        position = next;

        // ---- Head (relative to body) ----
        float hy = Mathf.Clamp(cmd.headYaw, -headYawLimit, headYawLimit);
        float hp = Mathf.Clamp(cmd.headPitch, -headPitchUpLimit, headPitchDownLimit);
        float prevHY = headYaw, prevHP = headPitch;
        headYaw = SmoothCapped(headYaw, hy, ref headYawVel, p.headSmoothTime, p.headMaxSpeed, dt);
        headPitch = SmoothCapped(headPitch, hp, ref headPitchVel, p.headSmoothTime, p.headMaxSpeed, dt);
        headYaw = Mathf.Clamp(Mathf.DeltaAngle(0f, headYaw), -headYawLimit, headYawLimit);
        headPitch = Mathf.Clamp(Mathf.DeltaAngle(0f, headPitch), -headPitchUpLimit, headPitchDownLimit);
        headYawRate = Mathf.DeltaAngle(prevHY, headYaw) / dt;
        headPitchRate = Mathf.DeltaAngle(prevHP, headPitch) / dt;
    }

    /// <summary>SmoothDampAngle with a hard cap on angular speed (deg/s).</summary>
    private static float SmoothCapped(float current, float target, ref float vel, float smoothTime, float maxRate, float dt)
    {
        float s = Mathf.SmoothDampAngle(current, target, ref vel, Mathf.Max(0.01f, smoothTime), Mathf.Infinity, dt);
        float delta = Mathf.Clamp(Mathf.DeltaAngle(current, s), -maxRate * dt, maxRate * dt);
        vel = Mathf.Clamp(vel, -maxRate, maxRate);
        return Mathf.Repeat(current + delta + 180f, 360f) - 180f;
    }
}
