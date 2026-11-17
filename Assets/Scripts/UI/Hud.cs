using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AceBarcade
{
    /// <summary>
    /// The one HUD every game uses: score, best, pause button, the "tap to start" screen, the
    /// pause screen and the game-over card. It reads the current game's state each frame, so
    /// games never touch it: they only add score and end the run.
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        const float OverDelay = 0.6f;

        Canvas _canvas;
        TextMeshProUGUI _score, _best, _title, _blurb, _tapToStart;
        TextMeshProUGUI _overScore, _overBest, _newBest;
        GameObject _top, _ready, _paused, _over, _dim, _pauseButton;
        int _shownScore = -1;
        float _overFor;
        ArcadeGame.Phase _lastState;

        public static Hud Create(Transform parent, Camera cam)
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<Hud>();
            hud.Build(cam);
            return hud;
        }

        void Build(Camera cam)
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = 5f;
            _canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiKit.ReferenceWidth, UiKit.ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f; // the world is a fixed width, so the UI is too
            gameObject.AddComponent<GraphicRaycaster>();

            // Dims the game behind the pause and game-over screens, and blocks touches to it.
            var dim = UiKit.MakeImage(transform, "Dim", Shapes.Square, Palette.Ink.WithAlpha(0.72f), blocksTouches: true);
            dim.rectTransform.Fill();
            _dim = dim.gameObject;

            var safe = UiKit.Rect(transform, "SafeArea").Fill();
            safe.gameObject.AddComponent<SafeArea>();

            BuildTop(safe);
            BuildReady(safe);
            BuildPaused(safe);
            BuildOver(safe);
            HideAll();
        }

        void BuildTop(RectTransform safe)
        {
            var top = UiKit.Rect(safe, "Top").At(new Vector2(0.5f, 1f), Vector2.zero, new Vector2(UiKit.ReferenceWidth, 180));
            _top = top.gameObject;

            _score = UiKit.MakeText(top, "Score", "0", 120, Palette.Paper);
            _score.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(500, 150));

            _best = UiKit.MakeText(top, "Best", "BEST 0", 46, Palette.Cloud);
            _best.alignment = TextAlignmentOptions.Left;
            _best.rectTransform.At(new Vector2(0f, 1f), new Vector2(44, -52), new Vector2(320, 70));

            _pauseButton = UiKit.MakeIconButton(top, "Pause", "pause", () => Arcade.Current?.Pause()).gameObject;
            ((RectTransform)_pauseButton.transform).At(new Vector2(1f, 1f), new Vector2(-36, -30), new Vector2(120, 120));
        }

        void BuildReady(RectTransform safe)
        {
            var ready = UiKit.Rect(safe, "Ready").At(new Vector2(0.5f, 0.5f), new Vector2(0, 260), new Vector2(UiKit.ReferenceWidth, 520));
            _ready = ready.gameObject;
            _title = UiKit.MakeText(ready, "Title", "", 124, Palette.Paper);
            _title.rectTransform.At(new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1000, 160));
            _blurb = UiKit.MakeText(ready, "Blurb", "", 52, Palette.Cloud);
            _blurb.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(1000, 80));
            _tapToStart = UiKit.MakeText(ready, "TapToStart", "TAP TO START", 76, Palette.Yellow);
            _tapToStart.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(1000, 110));
        }

        void BuildPaused(RectTransform safe)
        {
            var paused = UiKit.Rect(safe, "Paused").Fill();
            _paused = paused.gameObject;
            UiKit.MakeText(paused, "Title", "PAUSED", 130, Palette.Paper)
                .rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 170));
            ((RectTransform)UiKit.MakeButton(paused, "RESUME", UiKit.ButtonKind.Primary, () => Arcade.Current?.Resume()).transform)
                .At(new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(480, 150));
            ((RectTransform)UiKit.MakeButton(paused, "ARCADE", UiKit.ButtonKind.Secondary, Arcade.ShowHub).transform)
                .At(new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(480, 150));
        }

        void BuildOver(RectTransform safe)
        {
            var over = UiKit.Rect(safe, "Over").Fill();
            _over = over.gameObject;

            var card = UiKit.MakeImage(over, "Card", Sprites.Get("Shared/UI/card"), Color.white);
            card.rectTransform.At(new Vector2(0.5f, 0.5f), new Vector2(0, 160), new Vector2(840, 660));
            var c = card.rectTransform;

            UiKit.MakeText(c, "Title", "GAME OVER", 100, Palette.Paper)
                .rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(800, 130));
            UiKit.MakeText(c, "ScoreLabel", "SCORE", 50, Palette.Slate)
                .rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0, -200), new Vector2(800, 70));
            _overScore = UiKit.MakeText(c, "Score", "0", 190, Palette.Yellow);
            _overScore.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0, -260), new Vector2(800, 220));
            _overBest = UiKit.MakeText(c, "Best", "BEST 0", 58, Palette.Slate);
            _overBest.rectTransform.At(new Vector2(0.5f, 0f), new Vector2(0, 62), new Vector2(800, 80));

            _newBest = UiKit.MakeText(c, "NewBest", "NEW BEST!", 70, Palette.Pink);
            _newBest.rectTransform.At(new Vector2(1f, 1f), new Vector2(30, 20), new Vector2(420, 100));
            _newBest.rectTransform.localRotation = Quaternion.Euler(0, 0, -10f);

            ((RectTransform)UiKit.MakeButton(over, "RETRY", UiKit.ButtonKind.Primary, Arcade.Retry).transform)
                .At(new Vector2(0.5f, 0.5f), new Vector2(0, -330), new Vector2(480, 150));
            ((RectTransform)UiKit.MakeButton(over, "ARCADE", UiKit.ButtonKind.Secondary, Arcade.ShowHub).transform)
                .At(new Vector2(0.5f, 0.5f), new Vector2(0, -510), new Vector2(480, 150));
        }

        void HideAll()
        {
            _top.SetActive(false);
            _ready.SetActive(false);
            _paused.SetActive(false);
            _over.SetActive(false);
            _dim.SetActive(false);
        }

        void Update()
        {
            var g = Arcade.Current;
            if (g == null) { HideAll(); return; }

            if (g.Info != null && _title.text != g.Info.Title.ToUpperInvariant())
            {
                _title.text = g.Info.Title.ToUpperInvariant();
                _blurb.text = g.Info.Meta.Blurb;
                _shownScore = -1;
            }

            var state = g.State;
            bool entered = state != _lastState;
            _lastState = state;

            _top.SetActive(true);
            _pauseButton.SetActive(state == ArcadeGame.Phase.Playing);
            _ready.SetActive(state == ArcadeGame.Phase.Ready);
            _paused.SetActive(state == ArcadeGame.Phase.Paused);

            if (g.Score != _shownScore)
            {
                if (_shownScore >= 0 && g.Score > _shownScore) Juice.Squash(_score.transform, 0.25f);
                _shownScore = g.Score;
                _score.text = g.Score.ToString();
            }
            _best.text = $"BEST {Scores.Best(g.Info.Id)}";

            if (state == ArcadeGame.Phase.Ready)
            {
                float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f);
                _tapToStart.transform.localScale = new Vector3(pulse, pulse, 1f);
            }

            if (state == ArcadeGame.Phase.Over)
            {
                if (entered) _overFor = 0f;
                bool wasShown = _overFor >= OverDelay;
                _overFor += Time.unscaledDeltaTime;
                bool show = _overFor >= OverDelay;
                _over.SetActive(show);
                if (show && !wasShown)
                {
                    _overScore.text = g.Score.ToString();
                    _overBest.text = $"BEST {Scores.Best(g.Info.Id)}";
                    _newBest.gameObject.SetActive(g.NewBest);
                    Juice.Pop(_over.transform, unscaledTime: true);
                }
            }
            else _over.SetActive(false);

            _dim.SetActive(state == ArcadeGame.Phase.Paused || (state == ArcadeGame.Phase.Over && _overFor >= OverDelay));
        }
    }
}
