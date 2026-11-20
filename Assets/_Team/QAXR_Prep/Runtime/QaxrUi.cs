using System;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace QAXR
{
    /// <summary>Tiny world-space UI kit shared by the in-VR panels and the zone builders: primitive keys that poke and ray both select, and 3D TMP labels.</summary>
    public static class QaxrUi
    {
        /// <summary>A square key with a label, selectable by poke or ray. Returns its renderer so callers can recolour it.</summary>
        public static Renderer Button(Transform parent, string name, string label, Vector3 localPos, float size, Action onPress)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            var key = GameObject.CreatePrimitive(PrimitiveType.Cube);
            key.name = "Key";
            key.transform.SetParent(root.transform, false);
            key.transform.localScale = new Vector3(size, size, 0.02f);

            Text(root.transform, label, new Vector3(0f, 0f, -0.011f), size * 0.6f, size * 1.5f);
            root.AddComponent<XRSimpleInteractable>().selectEntered.AddListener(_ => onPress());
            return key.GetComponent<Renderer>();
        }

        /// <summary>
        /// A 3D TMP label. lineHeight is in metres (TMP 3D font size 1 ≈ 0.1 m line height, so size = metres × 10).
        /// Readable from the -Z side of the parent.
        /// </summary>
        public static TextMeshPro Text(Transform parent, string text, Vector3 localPos, float lineHeight, float width)
        {
            var go = new GameObject("Text_" + text.Split(' ')[0]);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = lineHeight * 10f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(width, lineHeight * 1.5f);
            return tmp;
        }
    }
}
