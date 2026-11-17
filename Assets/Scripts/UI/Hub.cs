using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AceBarcade
{
    /// <summary>
    /// The arcade's main menu: a header, then every game as a card in a scrolling grid of
    /// <see cref="Columns"/> x <see cref="VisibleRows"/>. Cards are sized from the screen, so
    /// exactly that many fit on any phone. Each card shows the game's icon on the left and its
    /// number, controls, title and best score on the right. Until there are enough games to fill
    /// the first screen, the empty slots show "coming soon" cards.
    /// </summary>
    public sealed class Hub : MonoBehaviour
    {
        public const int Columns = 2;
        public const int VisibleRows = 6;

        const float HeaderHeight = 230f;
        const float Pad = 36f;      // left, right and bottom
        const float PadTop = 34f;
        const float Gap = 30f;      // between cards, both ways

        RectTransform _view, _content;
        GridLayoutGroup _grid;
        ScrollRect _scroll;
        GameObject _screen;
        Vector2 _laidOutFor;
        readonly List<HubCard> _cards = new List<HubCard>();
        readonly List<RectTransform> _soon = new List<RectTransform>();

        /// <summary>The game cards, in number order (not the "coming soon" ones).</summary>
        public IReadOnlyList<HubCard> Cards => _cards;
        public int ComingSoonCount => _soon.Count;
        public bool Showing => _screen.activeSelf;
        public GridLayoutGroup Grid => _grid;
        public RectTransform Viewport => _view;
        public RectTransform Content => _content;
        public ScrollRect Scroll => _scroll;

        public static Hub Create(Transform parent, Camera cam)
        {
            var canvas = UiKit.MakeCanvas(parent, "Hub", cam, 90);
            var hub = canvas.gameObject.AddComponent<Hub>();
            hub.Build();
            Arcade.HubShown += hub.Show;
            Arcade.GameLaunched += hub.Hide;
            return hub;
        }

        void OnDestroy()
        {
            Arcade.HubShown -= Show;
            Arcade.GameLaunched -= Hide;
        }

        void Build()
        {
            _screen = UiKit.Rect(transform, "Screen").Fill().gameObject;
            var screen = _screen.transform;

            // Background decoration, like the games': flat Slate circles, no outlines.
            Decor(screen, new Vector2(0.05f, 0.62f), 560);
            Decor(screen, new Vector2(1.0f, 0.25f), 420);
            Decor(screen, new Vector2(0.3f, 0.08f), 160);

            // The header band is made first so it sits behind everything in the safe area (the title).
            var band = UiKit.Rect(screen, "Header");
            var safe = UiKit.Rect(screen, "SafeArea").Fill();
            safe.gameObject.AddComponent<SafeArea>();

            BuildHeader(band, safe);
            BuildGrid(safe);
            Populate(GameRegistry.All);
            _screen.SetActive(false);
        }

        static void Decor(Transform parent, Vector2 anchor, float size)
        {
            var c = UiKit.MakeImage(parent, "Decor", Shapes.Circle, Palette.Slate);
            c.rectTransform.anchorMin = c.rectTransform.anchorMax = anchor;
            c.rectTransform.sizeDelta = new Vector2(size, size);
        }

        void BuildHeader(RectTransform band, RectTransform safe)
        {
            // A Blue band like the games' top wall, reaching up behind the notch.
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.anchoredPosition = Vector2.zero;
            band.sizeDelta = new Vector2(0f, HeaderHeight);
            band.gameObject.AddComponent<HeaderFollowsSafeArea>().Init(safe, HeaderHeight);

            UiKit.MakeImage(band, "Fill", Shapes.Square, Palette.Blue).rectTransform.Fill();
            var shade = UiKit.MakeImage(band, "Shade", Shapes.Square, Palette.BlueDark).rectTransform;
            shade.anchorMin = Vector2.zero; shade.anchorMax = new Vector2(1f, 0f);
            shade.pivot = new Vector2(0.5f, 0f); shade.anchoredPosition = new Vector2(0f, Style.OutlinePx); shade.sizeDelta = new Vector2(0f, 22f);
            var line = UiKit.MakeImage(band, "Outline", Shapes.Square, Palette.Ink).rectTransform;
            line.anchorMin = Vector2.zero; line.anchorMax = new Vector2(1f, 0f);
            line.pivot = new Vector2(0.5f, 0f); line.anchoredPosition = Vector2.zero; line.sizeDelta = new Vector2(0f, Style.OutlinePx);

            var title = UiKit.MakeText(safe, "Title", "ACE BARCADE", 116, Palette.Yellow);
            title.rectTransform.At(new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1000f, 150f));
        }

        void BuildGrid(RectTransform safe)
        {
            var scrollRt = UiKit.Rect(safe, "Scroll");
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = new Vector2(0f, -HeaderHeight);

            _view = UiKit.Rect(scrollRt, "Viewport").Fill();
            _view.gameObject.AddComponent<RectMask2D>();
            // A clear image so a drag anywhere in the grid (even between cards) scrolls it.
            UiKit.MakeImage(_view, "Touch", Shapes.Square, Color.clear, blocksTouches: true).rectTransform.Fill();

            _content = UiKit.Rect(_view, "Content");
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _grid = _content.gameObject.AddComponent<GridLayoutGroup>();
            _grid.padding = new RectOffset((int)Pad, (int)Pad, (int)PadTop, (int)Pad);
            _grid.spacing = new Vector2(Gap, Gap);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = Columns;
            _grid.childAlignment = TextAnchor.UpperCenter;
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _view;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.scrollSensitivity = 40f;
        }

        /// <summary>Fills the grid with a card per game (tests pass their own list to try many games).</summary>
        public void Populate(IReadOnlyList<GameInfo> games)
        {
            foreach (Transform child in _content) Destroy(child.gameObject);
            _cards.Clear();
            _soon.Clear();
            _laidOutFor = Vector2.zero;

            foreach (var game in games)
                _cards.Add(HubCard.Create(_content, game));

            int slots = Mathf.Max(Columns * VisibleRows, Mathf.CeilToInt(_cards.Count / (float)Columns) * Columns);
            for (int i = _cards.Count; i < slots; i++)
                _soon.Add(HubCard.CreateComingSoon(_content));
        }

        void Show()
        {
            _screen.SetActive(true);
            foreach (var card in _cards) card.Refresh();
            Arcade.Camera.Background = Palette.Night;
        }

        void Hide(ArcadeGame _) => _screen.SetActive(false);

        void LateUpdate()
        {
            if (!Showing) return;
            var size = _view.rect.size;
            if (size != _laidOutFor && size.y > 0f) Layout(size);
        }

        void Update()
        {
            if (!Showing || Arcade.Input == null || !Arcade.Input.Back) return;
#if !UNITY_EDITOR
            Application.Quit();
#endif
        }

        /// <summary>Size the cards so exactly Columns x VisibleRows fill the viewport.</summary>
        void Layout(Vector2 view)
        {
            _laidOutFor = view;
            float w = (view.x - Pad * 2f - Gap * (Columns - 1)) / Columns;
            float h = (view.y - PadTop - Pad - Gap * (VisibleRows - 1)) / VisibleRows;
            _grid.cellSize = new Vector2(w, h);
            foreach (var card in _cards) card.Layout(_grid.cellSize);
        }
    }

    /// <summary>One game's card in the hub. Tapping it starts the game.</summary>
    public sealed class HubCard : MonoBehaviour
    {
        const float Inset = 22f;

        public GameInfo Info { get; private set; }
        public Button Button { get; private set; }
        public TextMeshProUGUI Title { get; private set; }
        public TextMeshProUGUI Best { get; private set; }

        RectTransform _thumb, _text;

        public static HubCard Create(Transform parent, GameInfo game)
        {
            var bg = UiKit.MakeSliced(parent, game.Title, "tile_sliced", blocksTouches: true);
            var card = bg.gameObject.AddComponent<HubCard>();
            card.Info = game;
            card.Button = bg.gameObject.AddComponent<Button>();
            card.Button.transition = Selectable.Transition.None;
            card.Button.onClick.AddListener(() => Arcade.Launch(game));
            bg.gameObject.AddComponent<PressScale>();

            var icon = Sprites.Get($"Games/{game.Folder}/icon");
            card._thumb = UiKit.MakeImage(bg.transform, "Thumbnail", icon, icon != null ? Color.white : Palette.Slate).rectTransform;
            card._thumb.anchorMin = card._thumb.anchorMax = new Vector2(0f, 0.5f);
            card._thumb.pivot = new Vector2(0f, 0.5f);

            card._text = UiKit.Rect(bg.transform, "Text");
            card._text.anchorMin = Vector2.zero;
            card._text.anchorMax = Vector2.one;

            var number = UiKit.MakeText(card._text, "Number", $"{game.Number:000} • {game.Meta.Controls.ToString().ToUpperInvariant()}", 32, Palette.Slate);
            number.alignment = TextAlignmentOptions.TopLeft;
            number.rectTransform.Fill();

            card.Title = UiKit.MakeText(card._text, "Title", game.Title.ToUpperInvariant(), 48, Palette.Ink);
            card.Title.alignment = TextAlignmentOptions.Left;
            card.Title.textWrappingMode = TextWrappingModes.Normal;
            card.Title.enableAutoSizing = true;
            card.Title.fontSizeMin = 30;
            card.Title.fontSizeMax = 50;
            card.Title.lineSpacing = -18f;
            card.Title.rectTransform.anchorMin = new Vector2(0f, 0.22f);
            card.Title.rectTransform.anchorMax = new Vector2(1f, 0.8f);
            card.Title.rectTransform.offsetMin = card.Title.rectTransform.offsetMax = Vector2.zero;

            card.Best = UiKit.MakeText(card._text, "Best", "", 32, Palette.Slate);
            card.Best.alignment = TextAlignmentOptions.BottomLeft;
            card.Best.rectTransform.Fill();

            card.Refresh();
            return card;
        }

        public static RectTransform CreateComingSoon(Transform parent)
        {
            var bg = UiKit.MakeSliced(parent, "ComingSoon", "tile_locked_sliced");
            var t = UiKit.MakeText(bg.transform, "Label", "COMING SOON", 40, Palette.Mist);
            t.rectTransform.Fill();
            t.rectTransform.offsetMin = new Vector2(0f, 14f); // sit above the shade side
            return bg.rectTransform;
        }

        public void Refresh() => Best.text = $"BEST {Scores.Best(Info.Id)}";

        /// <summary>Thumbnail as tall as the card allows (minus the inset); text fills the rest.</summary>
        public void Layout(Vector2 size)
        {
            float thumb = size.y - Inset * 2f - Style.OutlinePx;
            _thumb.sizeDelta = new Vector2(thumb, thumb);
            _thumb.anchoredPosition = new Vector2(Inset, Style.OutlinePx / 2f);
            _text.offsetMin = new Vector2(Inset + thumb + 20f, Inset + 12f);
            _text.offsetMax = new Vector2(-Inset, -Inset);
        }
    }

    /// <summary>Keeps the header band covering everything above the safe area plus its own height.</summary>
    sealed class HeaderFollowsSafeArea : MonoBehaviour
    {
        RectTransform _safe, _rt;
        float _height;

        public void Init(RectTransform safe, float height)
        {
            _safe = safe;
            _rt = (RectTransform)transform;
            _height = height;
        }

        void LateUpdate()
        {
            if (_safe == null) return;
            var parent = (RectTransform)_rt.parent;
            float above = parent.rect.height * (1f - _safe.anchorMax.y);
            _rt.sizeDelta = new Vector2(0f, _height + above);
        }
    }
}
