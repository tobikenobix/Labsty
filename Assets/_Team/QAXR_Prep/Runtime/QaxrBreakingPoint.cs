using UnityEngine;

namespace QAXR
{
    /// <summary>The eight MVP bug oracles (Roadmap §4.5).</summary>
    public enum Oracle
    {
        RuntimeException, InteractionFailed, TeleportIntoGeometry, HeldObjectClipping,
        FallThrough, Stuck, Unreachable, TextOrUiTooSmall
    }

    /// <summary>
    /// Ground truth: marks a deliberately planted bug and the oracle that should catch it.
    /// After a run, every marker should have a matching issue near it (oracle recall); issues far
    /// from any marker in the demo scene are false positives or bugs we didn't plant.
    /// </summary>
    public class QaxrBreakingPoint : MonoBehaviour
    {
        public Oracle oracle;
        [TextArea] public string description;

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
        }
    }
}
