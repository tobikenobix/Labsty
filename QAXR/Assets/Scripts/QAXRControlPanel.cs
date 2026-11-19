using UnityEngine;

/// <summary>
/// The control panel in Zone C. The agent's task is to find it, approach it, look at it and press its button.
/// Shared between agents: Occupant marks which agent is currently using it (others wait).
/// </summary>
public class QAXRControlPanel : MonoBehaviour
{
    public Transform button;
    public Renderer buttonRenderer;
    public Color idleColor = new Color(0.93f, 0.33f, 0.31f);
    public Color pressedColor = new Color(0.30f, 0.85f, 0.45f);

    public bool Pressed { get; private set; }
    public int PressCount { get; private set; }
    public QAXRAgentController Occupant { get; set; }

    private MaterialPropertyBlock block;

    public void ResetPanel()
    {
        Pressed = false;
        PressCount = 0;
        Occupant = null;
        ApplyColor(idleColor);
    }

    public void Press(QAXRAgentController by = null)
    {
        PressCount++;
        if (Pressed) return;
        Pressed = true;
        ApplyColor(pressedColor);
    }

    public Vector3 LookPoint => button != null ? button.position : transform.position;

    private void ApplyColor(Color c)
    {
        if (buttonRenderer == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        buttonRenderer.GetPropertyBlock(block);
        block.SetColor("_Color", c);
        block.SetColor("_BaseColor", c);
        buttonRenderer.SetPropertyBlock(block);
    }
}
