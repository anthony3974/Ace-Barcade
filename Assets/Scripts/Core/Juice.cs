using UnityEngine;

namespace AceBarcade
{
    /// <summary>
    /// The shared game feel. Every game uses these so hits, pops and bursts feel the same
    /// across the arcade. Strengths come from <see cref="Style"/>.
    /// </summary>
    public static class Juice
    {
        /// <summary>Squash a thing flat then spring back (a landing or a hit).</summary>
        public static void Squash(Transform t, float amount = Style.Squash)
        {
            var s = t.GetComponent<Squasher>();
            if (s == null) s = t.gameObject.AddComponent<Squasher>();
            s.Kick(amount);
        }

        /// <summary>Grow a thing in from nothing with a little overshoot (spawning, UI appearing).</summary>
        public static void Pop(Transform t, bool unscaledTime = false)
        {
            var p = t.GetComponent<Popper>();
            if (p == null) p = t.gameObject.AddComponent<Popper>();
            p.Play(unscaledTime);
        }

        public static void ShakeSmall() { if (Arcade.Camera != null) Arcade.Camera.Shake(Style.ShakeSmall); }
        public static void ShakeBig() { if (Arcade.Camera != null) Arcade.Camera.Shake(Style.ShakeBig); }

        /// <summary>A burst of flat palette dots flying out from a point.</summary>
        public static void Burst(Transform parent, Vector2 position, Color color, int count = 10, float speed = 4f)
        {
            for (int i = 0; i < count; i++)
            {
                float size = Random.Range(0.14f, 0.3f);
                var sr = Shapes.Make(parent, Shapes.Circle, color, position, new Vector2(size, size), 30, "Dot");
                var dot = sr.gameObject.AddComponent<BurstDot>();
                float angle = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
                dot.Velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed * Random.Range(0.6f, 1.1f);
            }
        }

        /// <summary>Back-out easing: overshoots a little past 1, then settles.</summary>
        public static float BackOut(float t)
        {
            const float s = 1.70158f;
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }
    }

    sealed class Squasher : MonoBehaviour
    {
        Vector3 _base;
        float _t = 1f, _amount;

        void Awake() => _base = transform.localScale;

        public void Kick(float amount)
        {
            _amount = amount;
            _t = 0f;
        }

        void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.deltaTime / 0.28f);
            float k = _amount * Mathf.Cos(_t * Mathf.PI * 2.5f) * (1f - _t) * (1f - _t);
            transform.localScale = new Vector3(_base.x * (1f + k), _base.y * (1f - k), _base.z);
        }
    }

    sealed class Popper : MonoBehaviour
    {
        Vector3 _base;
        float _t = 1f;
        bool _unscaled, _hasBase;

        public void Play(bool unscaled)
        {
            if (!_hasBase) { _base = transform.localScale; _hasBase = true; }
            _unscaled = unscaled;
            _t = 0f;
            transform.localScale = Vector3.zero;
        }

        void Update()
        {
            if (_t >= 1f) return;
            _t = Mathf.Min(1f, _t + (_unscaled ? Time.unscaledDeltaTime : Time.deltaTime) / Style.PopTime);
            transform.localScale = _base * Juice.BackOut(_t);
        }
    }

    sealed class BurstDot : MonoBehaviour
    {
        public Vector2 Velocity;
        float _life = 0.45f, _age;
        Vector3 _size;

        void Start() => _size = transform.localScale;

        void Update()
        {
            _age += Time.deltaTime;
            if (_age >= _life) { Destroy(gameObject); return; }
            Velocity *= 1f - 4f * Time.deltaTime;
            transform.localPosition += (Vector3)(Velocity * Time.deltaTime);
            transform.localScale = _size * (1f - _age / _life);
        }
    }
}
