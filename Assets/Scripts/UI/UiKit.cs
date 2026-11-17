using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AceBarcade
{
    /// <summary>
    /// Builds the arcade's UI pieces the one shared way: the one font (with its Ink outline),
    /// the shared button sprites, and sizes in reference pixels (the screen is 1080 px wide,
    /// the same as a full-screen SVG mock, so a 480 x 150 px SVG button is 480 x 150 here).
    /// </summary>
    public static class UiKit
    {
        public const float ReferenceWidth = 1080f;
        public const float ReferenceHeight = 1920f;

        static TMP_FontAsset _font;

        /// <summary>Lilita One, made into a TextMeshPro font by ProjectSetup. Its material has the Ink outline.</summary>
        public static TMP_FontAsset Font => _font != null ? _font : _font = Resources.Load<TMP_FontAsset>("Fonts/LilitaOne SDF");

        public enum ButtonKind { Primary, Secondary }

        /// <summary>
        /// A full-screen canvas drawn by the arcade camera, 1080 px wide on every phone. Sprites
        /// map 1:1 to reference px (120 px per unit, like the art), so a 9-slice's 8 px outline
        /// stays 8 px.
        /// </summary>
        public static Canvas MakeCanvas(Transform parent, string name, Camera cam, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 5f;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f; // the world is a fixed width, so the UI is too
            scaler.referencePixelsPerUnit = Style.PixelsPerUnit;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>A stretchy 9-slice image from Art/Shared/UI (a name ending in "_sliced").</summary>
        public static Image MakeSliced(Transform parent, string name, string sprite, bool blocksTouches = false)
        {
            var img = MakeImage(parent, name, Sprites.Get("Shared/UI/" + sprite), Color.white, blocksTouches);
            img.type = Image.Type.Sliced;
            return img;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Stretch to fill the parent.</summary>
        public static RectTransform Fill(this RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Anchor and pivot at one point of the parent (0-1 each way), offset in reference px.</summary>
        public static RectTransform At(this RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            return rt;
        }

        public static TextMeshProUGUI MakeText(Transform parent, string name, string text, float size, Color color)
        {
            var rt = Rect(parent, name);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        public static Image MakeImage(Transform parent, string name, Sprite sprite, Color color, bool blocksTouches = false)
        {
            var img = Rect(parent, name).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = blocksTouches;
            return img;
        }

        /// <summary>A shared button (480 x 150): Primary is green, Secondary is cloud grey. Text is Ink.</summary>
        public static Button MakeButton(Transform parent, string label, ButtonKind kind, Action onClick)
        {
            var sprite = Sprites.Get(kind == ButtonKind.Primary ? "Shared/UI/button_primary" : "Shared/UI/button_secondary");
            var img = MakeImage(parent, label, sprite, Color.white, blocksTouches: true);
            img.rectTransform.sizeDelta = new Vector2(480, 150);
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => onClick());
            img.gameObject.AddComponent<PressScale>();

            var t = MakeText(img.transform, "Label", label, 64, Palette.Ink);
            t.rectTransform.Fill();
            t.rectTransform.offsetMin = new Vector2(0, 14); // sit above the button's shadow side
            return b;
        }

        /// <summary>A round icon button (120 x 120) from Art/Shared/UI.</summary>
        public static Button MakeIconButton(Transform parent, string name, string sprite, Action onClick)
        {
            var img = MakeImage(parent, name, Sprites.Get("Shared/UI/" + sprite), Color.white, blocksTouches: true);
            img.rectTransform.sizeDelta = new Vector2(120, 120);
            var b = img.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() => onClick());
            img.gameObject.AddComponent<PressScale>();
            return b;
        }
    }

    /// <summary>Buttons shrink a little while held, the same everywhere.</summary>
    sealed class PressScale : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public void OnPointerDown(PointerEventData e) => transform.localScale = Vector3.one * 0.92f;
        public void OnPointerUp(PointerEventData e) => transform.localScale = Vector3.one;
        public void OnPointerExit(PointerEventData e) => transform.localScale = Vector3.one;
    }

    /// <summary>Keeps its children inside the phone's safe area (clear of notches and rounded corners).</summary>
    [RequireComponent(typeof(RectTransform))]
    sealed class SafeArea : MonoBehaviour
    {
        Rect _applied;

        void Update()
        {
            var safe = Screen.safeArea;
            if (safe == _applied || Screen.width == 0 || Screen.height == 0) return;
            _applied = safe;
            var rt = (RectTransform)transform;
            rt.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rt.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
