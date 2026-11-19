using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace QAXR
{
    /// <summary>Breaking point for the runtime-exception oracle: pressing this button throws a NullReferenceException.</summary>
    [RequireComponent(typeof(XRSimpleInteractable))]
    public class ThrowingButton : MonoBehaviour
    {
        XRSimpleInteractable button;

        void Awake() => button = GetComponent<XRSimpleInteractable>();
        void OnEnable() => button.selectEntered.AddListener(OnPressed);
        void OnDisable() => button.selectEntered.RemoveListener(OnPressed);

        void OnPressed(SelectEnterEventArgs args)
        {
            Transform target = null;      // the "forgot to wire it in the Inspector" bug
            target.localScale = Vector3.one; // deliberate NullReferenceException
        }
    }
}
