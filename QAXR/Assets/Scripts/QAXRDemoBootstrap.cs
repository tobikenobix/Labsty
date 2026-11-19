using UnityEngine;

/// <summary>
/// Fallback: in case the first-open Editor scene builder did not run,
/// the included starter scene can still be played immediately.
/// </summary>
public sealed class QAXRDemoBootstrap : MonoBehaviour
{
    private void Awake()
    {
        if (FindAnyObjectByType<QAXRScenarioController>() == null)
            QAXRSceneBuilder.Build();
    }
}
