using System.Threading.Tasks;
using QAXR.Demo;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

public static class Smoke
{
    static async Task Frames(int n) { for (int i = 0; i < n; i++) await Task.Yield(); }
    static async Task Seconds(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) await Task.Yield(); }

    public static async Task<string> Run()
    {
        var log = new System.Text.StringBuilder();
        var o = Object.FindFirstObjectByType<XROrigin>();
        var move = Object.FindFirstObjectByType<ContinuousMoveProvider>();
        var cc = o.GetComponent<CharacterController>();
        void Place(Vector3 p) { cc.enabled = false; o.transform.position = p; cc.enabled = true; }
        log.AppendLine($"cam local y={o.Camera.transform.localPosition.y:F2} appSpeed={move.moveSpeed}");

        // Zone 1 bad: walk the origin forward by hand, speed should ramp up.
        Place(new Vector3(0, 0, 5));
        for (int i = 0; i < 120; i++) { o.transform.position += Vector3.forward * 0.05f; await Task.Yield(); }
        log.AppendLine($"Z1 bad moveSpeed after ~2s moving = {move.moveSpeed:F2}");
        Object.FindFirstObjectByType<Zone1FastCorridor>().isFixed = true;
        await Frames(3);
        log.AppendLine($"Z1 fixed moveSpeed = {move.moveSpeed:F2}");
        Object.FindFirstObjectByType<Zone1FastCorridor>().isFixed = false;
        Place(new Vector3(0, 0, -1)); await Frames(3);
        log.AppendLine($"outside moveSpeed = {move.moveSpeed:F2}");

        // Zone 2: stand on deck, should be carried and rolled.
        Place(new Vector3(0, 0.3f, 63)); await Seconds(1.5f);
        log.AppendLine($"Z2 parent={o.transform.parent?.name} roll={o.transform.eulerAngles.z:F1}");
        Place(new Vector3(0, 0, 72)); await Frames(3);
        log.AppendLine($"Z2 left: parent={o.transform.parent?.name ?? "none"} roll={o.transform.eulerAngles.z:F1}");

        // Zone 3: walk into the room, tracking should be taken over.
        var tpd = o.Camera.GetComponent<TrackedPoseDriver>();
        float yaw0 = o.transform.eulerAngles.y;
        Place(new Vector3(0, 0, 80)); await Seconds(2f);
        log.AppendLine($"Z3 tracking enabled={tpd.enabled} originYaw {yaw0:F0}->{o.transform.eulerAngles.y:F0}");
        await Seconds(4.5f);
        log.AppendLine($"Z3 after: tracking enabled={tpd.enabled}");
        return log.ToString();
    }
}
