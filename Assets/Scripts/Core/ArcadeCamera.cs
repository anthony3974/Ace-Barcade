using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// The one camera. Orthographic, centred on (0, 0), and always shows the whole
    /// <see cref="Style.WorldWidth"/> x <see cref="Style.WorldHeight"/> play area: taller phones
    /// see extra background above and below, wider screens see extra at the sides.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class ArcadeCamera : MonoBehaviour
    {
        public Camera Cam { get; private set; }

        /// <summary>Half the visible size, in units (at least 4.5 x 8).</summary>
        public Vector2 HalfSize { get; private set; }

        float _shake;
        Vector2 _shakeOffset;

        void Awake()
        {
            Cam = GetComponent<Camera>();
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 100f;
            gameObject.tag = "MainCamera";
            ResetView();
            Fit();
        }

        /// <summary>Back to the default view: no shake, the shared background color.</summary>
        public void ResetView()
        {
            if (Cam == null) return;
            _shake = 0f;
            _shakeOffset = Vector2.zero;
            Cam.backgroundColor = Palette.Night;
            transform.localPosition = new Vector3(0f, 0f, -10f);
        }

        public Color Background
        {
            get => Cam.backgroundColor;
            set => Cam.backgroundColor = value;
        }

        /// <summary>Shake the view. Use <see cref="Style.ShakeSmall"/> or <see cref="Style.ShakeBig"/>.</summary>
        public void Shake(float strength) => _shake = Mathf.Max(_shake, strength);

        public Vector2 ScreenToWorld(Vector2 screen) => Cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f)) - (Vector3)_shakeOffset;

        void LateUpdate()
        {
            Fit();
            _shakeOffset = _shake > 0.001f ? Random.insideUnitCircle * _shake : Vector2.zero;
            _shake = Mathf.MoveTowards(_shake, 0f, Time.unscaledDeltaTime * 1.5f);
            transform.localPosition = new Vector3(_shakeOffset.x, _shakeOffset.y, -10f);
        }

        void Fit()
        {
            float aspect = Cam.aspect;
            float halfH = Mathf.Max(Style.WorldHeight / 2f, Style.WorldWidth / 2f / aspect);
            Cam.orthographicSize = halfH;
            HalfSize = new Vector2(halfH * aspect, halfH);
        }
    }
}
