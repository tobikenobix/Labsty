using UnityEngine;

/// <summary>
/// Marks an object the simulated agent can perceive (waypoint, obstacle, panel, landmark, exit, other agent).
/// Contains only what a user could see: position, size, kind. It carries no fault or risk information.
/// </summary>
public class QAXRPerceivable : MonoBehaviour
{
    public QAXRPerceivableKind kind = QAXRPerceivableKind.Landmark;
    public string displayName = "Object";
    [Tooltip("Footprint radius on the floor (m). Used for obstacle avoidance.")]
    public float radius = 0.3f;
    [Tooltip("Where the head looks when attending to this object. Defaults to the transform.")]
    public Transform attentionPoint;
    [Tooltip("Floor point to stand on for interaction (ControlPanel). Defaults to the transform.")]
    public Transform approachPoint;
    [Tooltip("Max distance (m, floor plane) from the attention point at which interaction is possible.")]
    public float interactionRange = 0.95f;

    public Vector3 AttentionPosition => attentionPoint != null ? attentionPoint.position : transform.position;
    public Vector3 ApproachPosition => approachPoint != null ? approachPoint.position : transform.position;
    public Vector3 FloorPosition => new Vector3(transform.position.x, 0f, transform.position.z);

    /// <summary>Set for kind == Agent so the agent ignores itself.</summary>
    public QAXRAgentController Owner { get; set; }
}
