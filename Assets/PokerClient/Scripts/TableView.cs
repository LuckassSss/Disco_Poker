using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PokerClient
{
    /// <summary>
    /// UI собирается из кода. Арт — через [SerializeField] с объекта «Poker» в сцене (Art/UI).
    /// Карты рисуются программно (спрайтов карт в паке нет), скруглённый спрайт генерируется при старте.
    /// Читает только <see cref="TableModel"/> и состояние <see cref="PokerSession"/>.
    /// ponytail: когда придут спрайты карт и вёрстка от художника — заменить на префабы, логика чтения модели та же.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        [Header("Арт (Assets/PokerClient/Art/UI)")]
        [SerializeField] Sprite _loadingBackground; // Loading/loading screen_ no loading ui
        [SerializeField] Sprite _loadingPanel;      // Loading/loading-ui-miami-disco-no-text
        [SerializeField] Sprite[] _ballFrames;      // Animations/Balls/Frames/*
        [SerializeField] Sprite _tableBackground;   // Locations/Location01/Tables/table_1 (вид сверху, 5 кресел = 5 мест)

        [Header("Лобби: карта локаций и HUD")]
        [SerializeField] LocationArt[] _locations;  // Locations/Location01..03, порядок столов — как в TableRects
        [SerializeField] Sprite _hudBar;            // HUD/hud_top_background_empty
        [SerializeField] Sprite _currencyPanel;     // HUD/02_currency_empty-transparent
        [SerializeField] Sprite _avatar;            // HUD/03_player_avatar
        [SerializeField] Sprite _homeButton;        // HUD/01_home_button
        [SerializeField] Sprite _levelPanel;        // HUD/04_level_empty-transparent
        [SerializeField] Sprite _shopButton;        // HUD/05_shop_button
        [SerializeField] Sprite _starBadge;         // HUD/06_star_badge
        [SerializeField] Sprite _safeButton;        // HUD/07_safe_button
        [SerializeField] Sprite _bagButton;         // HUD/08_bag_button
        [SerializeField] Sprite _friendsButton;     // HUD/09_friends_button
        [SerializeField] Sprite _menuButton;        // HUD/10_menu_button
        [SerializeField] Sprite _activePanel;       // Map/active-table-panel-americana-80s
        [SerializeField] Sprite _lockPanel;         // Map/table3-lock-panel-transparent
        // ponytail: прогресс игрока сервер не присылает — уровень и число открытых локаций пока задаются в инспекторе
        [SerializeField] int _unlockedLocations = 1;
        [SerializeField] int _playerLevel = 1;

        [System.Serializable]
        public sealed class LocationArt
        {
            public Sprite Background;
            public Sprite[] Names; // по столам
        }

        // объекты-столы на карте: прямоугольник в долях фона (x, y от левого верхнего угла, w, h), снято с референсов W UI / NEW_VAR
        static readonly Rect[][] TableRects =
        {
            new[] { new Rect(0.09f, 0.175f, 0.22f, 0.245f), new Rect(0.365f, 0.32f, 0.227f, 0.37f), new Rect(0.688f, 0.39f, 0.22f, 0.255f) },
            new[] { new Rect(0.14f, 0.325f, 0.21f, 0.22f), new Rect(0.345f, 0.165f, 0.345f, 0.225f), new Rect(0.085f, 0.64f, 0.3f, 0.29f), new Rect(0.53f, 0.5f, 0.44f, 0.43f) },
            new[] { new Rect(0.055f, 0.3f, 0.305f, 0.42f), new Rect(0.65f, 0.66f, 0.25f, 0.3f), new Rect(0.765f, 0.085f, 0.17f, 0.37f), new Rect(0.31f, 0.15f, 0.24f, 0.33f) },
        };
        static readonly int[][] TableLevels = { new[] { 1, 2, 3 }, new[] { 1, 2, 3, 5 }, new[] { 1, 2, 3, 4 } };
        // ponytail: сервер локаций не знает — стол №i в локации идёт на уровень ставок по индексу
        static readonly string[] TableStakes = { "micro", "low", "mid", "mid" };

        const float BallFps = 12f;

        // экранные позиции мест в долях картинки стола (x, y от верхнего края): 0 — своё (снизу, привязано к краю экрана),
        // дальше по часовой стрелке по креслам на рендере: слева, верх-слева, верх-справа, справа (стол 5-max).
        static readonly Vector2[] SeatUV =
        {
            Vector2.zero, new(0.196f, 0.422f), new(0.374f, 0.16f), new(0.624f, 0.16f), new(0.802f, 0.422f),
        };
        static readonly Color Night = new(0.08f, 0.03f, 0.14f);
        static readonly Color Panel = new(0.06f, 0.02f, 0.12f, 0.72f);
        static readonly Color PanelActive = new(0.55f, 0.12f, 0.45f, 0.9f);
        static readonly Color Magenta = new(0.95f, 0.2f, 0.65f);
        static readonly Color Cyan = new(0.15f, 0.7f, 0.9f);
        static readonly Color Gold = new(1f, 0.85f, 0.4f);
        static readonly Color Grey = new(0.35f, 0.32f, 0.42f);
        static readonly Color CardBack = new(0.3f, 0.1f, 0.5f);
        static readonly Color CardRed = new(0.85f, 0.1f, 0.2f);
        static readonly Color CardBlack = new(0.1f, 0.08f, 0.15f);

        sealed class CardSlot
        {
            public GameObject Go;
            public Image Bg;
            public TMP_Text Corner, Pip;
        }

        PokerSession _s;
        TMP_FontAsset _font;
        bool _suitGlyphs;
        Sprite _rounded;

        GameObject _lobby, _table, _loading;
        Image _ball;
        TMP_Text _loadingLabel;
        TMP_InputField _login, _password;
        GameObject _connectPanel;
        RectTransform _mapBackdrop, _map, _tables;
        readonly Dictionary<Sprite, Sprite> _blurred = new(); // размытые копии фонов для подложки
        TMP_Text _hudNick, _hudBalance;
        Button _prevLoc, _nextLoc;
        GameObject _menu; // выпадашка под «три точки»: пока только «Выйти»
        readonly List<Button> _tableButtons = new();
        int _location;

        readonly TMP_Text[] _seatText = new TMP_Text[TableModel.MaxSeats];
        readonly RectTransform[] _seatRt = new RectTransform[TableModel.MaxSeats];
        readonly Image[] _seatBg = new Image[TableModel.MaxSeats];
        readonly CardSlot[][] _seatCards = new CardSlot[TableModel.MaxSeats][];
        readonly CardSlot[] _board = new CardSlot[5];
        readonly CardSlot[] _myCards = new CardSlot[2];
        TMP_Text _header, _pot, _status, _timer, _message, _sliderLabel;
        RectTransform _actions;
        GameObject _sliderRow;
        Slider _slider;
        string _sliderPrefix = "";

        int _drawnVersion = -1;
        ClientState _drawnState = (ClientState)(-1);
        bool _drawnPending;
        string _drawnMessage;

        void Start()
        {
            _s = GetComponent<PokerSession>();
            _font = TMP_Settings.instance != null ? TMP_Settings.defaultFontAsset : null;
            if (_font == null) _font = TMP_FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            // ♠♥♦♣ нет в статическом атласе, но есть в динамическом fallback-шрифте — искать и там
            _suitGlyphs = _font.HasCharacter('♠', true, true) && _font.HasCharacter('♥', true, true);
            _rounded = MakeRoundedSprite(16);
            Build();
        }

        void Update()
        {
            var m = _s.Model;
            if (m.Version != _drawnVersion || _s.State != _drawnState || _s.ActionPending != _drawnPending || _s.Message != _drawnMessage)
            {
                _drawnVersion = m.Version;
                _drawnState = _s.State;
                _drawnPending = _s.ActionPending;
                _drawnMessage = _s.Message;
                Redraw();
            }
            UpdateTimer();
            if (_loading.activeSelf && _ballFrames != null && _ballFrames.Length > 0)
                _ball.sprite = _ballFrames[(int)(Time.unscaledTime * BallFps) % _ballFrames.Length];
        }

        // ------------------------------------------------------------------ отрисовка
        void Redraw()
        {
            var m = _s.Model;
            var st = _s.State;
            var atTable = st == ClientState.AtTable || (st == ClientState.Reconnecting && m.AtTable);
            var loading = !atTable && st is ClientState.Connecting or ClientState.Joining or ClientState.Reconnecting;
            _loading.SetActive(loading);
            _lobby.SetActive(!atTable && !loading);
            _table.SetActive(atTable);
            _message.text = _s.Message;

            if (loading)
            {
                _loadingLabel.text = st switch
                {
                    ClientState.Connecting => "ПОДКЛЮЧЕНИЕ...",
                    ClientState.Joining => "ИЩЕМ СТОЛ...",
                    _ => "ПЕРЕПОДКЛЮЧЕНИЕ...",
                };
                return;
            }

            if (!atTable)
            {
                _connectPanel.SetActive(st == ClientState.Disconnected);
                _login.interactable = _password.interactable = st == ClientState.Disconnected;
                _hudNick.text = _s.Nickname;
                if (st != ClientState.Authorized) _menu.SetActive(false);
                _hudBalance.text = st == ClientState.Authorized ? Chips(m.Balance) : "—";
                foreach (var b in _tableButtons) b.interactable = st == ClientState.Authorized;
                return;
            }

            _header.text = $"Стол {m.TableId}   ставки: {m.Stake}   баланс: {m.Balance}";
            for (var i = 0; i < _board.Length; i++) SetCard(_board[i], i < m.Board.Count ? m.Board[i] : null);
            for (var i = 0; i < _myCards.Length; i++) SetCard(_myCards[i], i < m.HoleCards.Count ? m.HoleCards[i] : null);
            _pot.text = $"Банк: {m.Pot}";
            _status.text = string.IsNullOrEmpty(m.Street) ? m.Status : $"{m.Street}   {m.Status}";

            for (var i = 0; i < TableModel.MaxSeats; i++)
            {
                var s = m.Seats[i];
                var pos = TableModel.ScreenPos(i, m.MySeat);
                var text = _seatText[pos];
                var bg = _seatBg[pos];
                var cards = _seatCards[pos];
                if (!s.Occupied)
                {
                    text.text = "<alpha=#60>пусто";
                    text.alpha = 1f;
                    bg.color = Panel;
                    SetCard(cards[0], null);
                    SetCard(cards[1], null);
                    continue;
                }
                var folded = s.State == "folded";
                var sb = new StringBuilder();
                sb.Append(i == m.MySeat ? "<b>" : "").Append(s.Nickname).Append(i == m.MySeat ? "</b>" : "");
                if (i == m.ButtonSeat) sb.Append("  <color=#FFD54F>D</color>");
                sb.Append('\n').Append(s.Stack);
                if (s.Bet > 0) sb.Append("   <color=#FFD54F>ставка ").Append(s.Bet).Append("</color>");
                sb.Append('\n');
                if (!s.Connected) sb.Append("<color=#FF8A80>оффлайн</color>");
                else if (s.SittingOut) sb.Append("<alpha=#A0>сит-аут");
                else sb.Append(s.LastAction);
                text.text = sb.ToString();
                text.alpha = folded ? 0.45f : 1f;
                bg.color = i == m.ToAct ? PanelActive : Panel;

                // свои карты — отдельно и крупно, у других: вскрытые или рубашки
                string c0 = null, c1 = null;
                if (i != m.MySeat && !folded)
                {
                    if (s.ShownCards != null && s.ShownCards.Count >= 2) { c0 = s.ShownCards[0]; c1 = s.ShownCards[1]; }
                    else if (s.HasCards) c0 = c1 = "back";
                }
                SetCard(cards[0], c0);
                SetCard(cards[1], c1);
            }

            RebuildActions(m);
        }

        void RebuildActions(TableModel m)
        {
            foreach (Transform child in _actions) Destroy(child.gameObject);
            _sliderRow.SetActive(false);
            if (!m.MyTurn) return;

            var x = -450f;
            foreach (var o in m.Options)
            {
                var option = o;
                string label;
                Color color;
                UnityAction onClick;
                switch (o.Action)
                {
                    case "fold": label = "Фолд"; color = Grey; onClick = () => _s.SendAction("fold", 0); break;
                    case "check": label = "Чек"; color = Cyan; onClick = () => _s.SendAction("check", 0); break;
                    case "call": label = $"Колл {o.Min}"; color = Cyan; onClick = () => _s.SendAction("call", 0); break;
                    case "all_in": label = $"Олл-ин {o.Min}"; color = Magenta; onClick = () => _s.SendAction("all_in", 0); break;
                    case "bet":
                    case "raise":
                        label = o.Action == "bet" ? "Бет" : "Рейз";
                        color = Magenta;
                        onClick = () => _s.SendAction(option.Action, (long)_slider.value);
                        _sliderRow.SetActive(true);
                        _sliderPrefix = o.Action == "bet" ? "Бет" : "Рейз до";
                        _slider.minValue = o.Min;
                        _slider.maxValue = o.Max;
                        _slider.SetValueWithoutNotify(o.Min);
                        _sliderLabel.text = $"{_sliderPrefix}: {o.Min}";
                        break;
                    default: continue;
                }
                var b = MakeButton(_actions, label, new Vector2(x, 0), new Vector2(280, 90), onClick, color);
                b.interactable = !_s.ActionPending;
                x += 300f;
            }
        }

        void UpdateTimer()
        {
            var m = _s.Model;
            if (!_table.activeSelf || m.ToAct < 0 || m.DeadlineTs <= 0)
            {
                _timer.text = "";
                return;
            }
            var left = m.DeadlineTs - _s.ServerNow;
            _timer.text = $"{Mathf.Max(0f, (float)left):0}";
            var seat = _seatRt[TableModel.ScreenPos(m.ToAct, m.MySeat)];
            if (_timer.transform.parent != seat) _timer.transform.SetParent(seat, false);
        }

        // ------------------------------------------------------------------ карты
        /// <param name="code">null — слота нет; "back" — рубашка; иначе "As", "Td"...</param>
        void SetCard(CardSlot slot, string code)
        {
            slot.Go.SetActive(code != null);
            if (code == null) return;
            if (code == "back" || code.Length < 2)
            {
                slot.Bg.color = CardBack;
                slot.Corner.text = slot.Pip.text = "";
                return;
            }
            var rank = code[0] == 'T' ? "10" : code[0].ToString();
            var suit = code[1];
            var glyph = _suitGlyphs
                ? suit switch { 's' => "♠", 'h' => "♥", 'd' => "♦", 'c' => "♣", _ => "?" }
                : suit.ToString();
            var color = suit is 'h' or 'd' ? CardRed : CardBlack;
            slot.Bg.color = Color.white;
            slot.Corner.text = $"{rank}\n{glyph}";
            slot.Corner.color = color;
            slot.Pip.text = glyph;
            slot.Pip.color = color;
        }

        CardSlot MakeCard(Transform parent, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            var rt = Box(parent, "Card", pos, size, anchor);
            var slot = new CardSlot { Go = rt.gameObject, Bg = MakePanel(rt, Color.white) };
            slot.Corner = AddText(Stretch(rt, "Corner"), size.y * 0.22f, TextAlignmentOptions.TopLeft, CardBlack);
            slot.Corner.margin = new Vector4(size.x * 0.08f, size.y * 0.04f, 0, 0);
            slot.Corner.lineSpacing = -20;
            slot.Pip = AddText(Stretch(rt, "Pip"), size.y * 0.5f, TextAlignmentOptions.BottomRight, CardBlack);
            slot.Pip.margin = new Vector4(0, 0, size.x * 0.08f, size.y * 0.02f);
            rt.gameObject.SetActive(false);
            return slot;
        }

        // ------------------------------------------------------------------ сборка UI
        void Build()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                DontDestroyOnLoad(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)));

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            MakePanel(Stretch(root, "Background"), Night);
            BuildLobby(root);
            BuildTable(root);
            BuildLoading(root);
            _message = AddText(Box(root, "Message", new Vector2(0, -104), new Vector2(1000, 40), new Vector2(0.5f, 1f)), 26, TextAlignmentOptions.Center, Gold);
        }

        void BuildLoading(Transform root)
        {
            _loading = Stretch(root, "Loading").gameObject;
            var t = _loading.transform;
            MakeCover(t, _loadingBackground, Night);
            // неоновая панель внизу: слева круг под шар, справа надпись
            var panel = Box(t, "Panel", new Vector2(0, 110), new Vector2(640, 256), new Vector2(0.5f, 0f));
            MakeImage(panel, _loadingPanel, new Color(0.15f, 0.05f, 0.25f, 0.9f));
            _ball = MakeImage(Box(panel, "Ball", new Vector2(-207, 0), new Vector2(130, 130)), _ballFrames is { Length: > 0 } ? _ballFrames[0] : null, Magenta);
            _loadingLabel = AddText(Box(panel, "Label", new Vector2(60, 0), new Vector2(420, 80)), 34, TextAlignmentOptions.Center);
            _loading.SetActive(false);
        }

        void BuildLobby(Transform root)
        {
            _lobby = Stretch(root, "Lobby").gameObject;
            var t = _lobby.transform;
            // карта целиком по высоте экрана, чтобы все столы были видны; боковые поля на 20:9 закрывает
            // сильно размытая копия той же картинки (резкая копия читалась как дубль краёв). Спрайты ставит ShowLocation.
            _mapBackdrop = MakeCover(t, null, Night);
            MakePanel(Stretch(t, "Dim"), new Color(0, 0, 0, 0.35f));
            _map = Stretch(t, "Map");
            MakeImage(_map, null, Night);
            _map.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _tables = Stretch(_map, "Tables");

            _prevLoc = MakeButton(t, "‹", new Vector2(24, 0), new Vector2(80, 160), () => ShowLocation(_location - 1), Panel, new Vector2(0f, 0.5f));
            _nextLoc = MakeButton(t, "›", new Vector2(-24, 0), new Vector2(80, 160), () => ShowLocation(_location + 1), Panel, new Vector2(1f, 0.5f));

            // HUD сверху по макету W UI: домой, баланс, аватар, (ник — наш), уровень | магазин | звезда, сейф, сумка, друзья, меню.
            // ponytail: магазин, звезда, сейф, сумка, друзья — картинки без действия, на сервере этих фич нет.
            var hud = Stretch(t, "Hud");
            hud.anchorMin = new Vector2(0, 1);
            hud.pivot = new Vector2(0.5f, 1f);
            hud.sizeDelta = new Vector2(0, 96);
            MakeImage(hud, _hudBar, Panel);
            var left = new Vector2(0f, 0.5f);
            var right = new Vector2(1f, 0.5f);
            SpriteButton(hud, "Home", new Vector2(24, 0), new Vector2(76, 70), left, _homeButton, () => ShowLocation(0));
            var currency = Box(hud, "Currency", new Vector2(116, 0), new Vector2(232, 90), left);
            MakeImage(currency, _currencyPanel, Panel);
            _hudBalance = AddText(Box(currency, "Balance", new Vector2(30, 2), new Vector2(150, 60)), 30, TextAlignmentOptions.Right, Gold);
            MakeImage(Box(hud, "Avatar", new Vector2(366, 0), new Vector2(80, 80), left), _avatar, Magenta);
            _hudNick = AddText(Box(hud, "Nick", new Vector2(460, 0), new Vector2(200, 60), left), 30, TextAlignmentOptions.Left, Gold);
            var level = Box(hud, "Level", new Vector2(676, 0), new Vector2(160, 70), left);
            MakeImage(level, _levelPanel, Panel).preserveAspect = true;
            AddText(Box(level, "Value", new Vector2(24, 2), new Vector2(90, 56)), 30, TextAlignmentOptions.Center, Gold).text = _playerLevel.ToString();
            MakeImage(Box(hud, "Shop", Vector2.zero, new Vector2(200, 88)), _shopButton, Panel).preserveAspect = true;
            MakeImage(Box(hud, "Star", new Vector2(-392, 0), new Vector2(76, 76), right), _starBadge, Panel).preserveAspect = true;
            MakeImage(Box(hud, "Safe", new Vector2(-282, 0), new Vector2(90, 75), right), _safeButton, Panel).preserveAspect = true;
            MakeImage(Box(hud, "Bag", new Vector2(-170, 0), new Vector2(92, 73), right), _bagButton, Panel).preserveAspect = true;
            MakeImage(Box(hud, "Friends", new Vector2(-72, 0), new Vector2(78, 78), right), _friendsButton, Panel).preserveAspect = true;
            SpriteButton(hud, "Menu", new Vector2(-24, 0), new Vector2(28, 78), right, _menuButton, () => _menu.SetActive(!_menu.activeSelf));

            var menu = Box(t, "Menu", new Vector2(-24, -104), new Vector2(220, 84), new Vector2(1f, 1f));
            MakePanel(menu, Panel);
            MakeButton(menu, "Выйти", Vector2.zero, new Vector2(196, 64), () => { _menu.SetActive(false); _s.Disconnect(); }, Grey);
            _menu = menu.gameObject;
            _menu.SetActive(false);

            // экран подключения на фоне заставки (как экран загрузки), только в Disconnected; карта — после авторизации
            _connectPanel = Stretch(t, "Connect").gameObject;
            MakeCover(_connectPanel.transform, _loadingBackground, Night);
            var panel = Box(_connectPanel.transform, "Panel", new Vector2(0, 40), new Vector2(980, 330), new Vector2(0.5f, 0f));
            MakePanel(panel, Panel);
            AddText(Box(panel, "LoginLabel", new Vector2(-330, 100), new Vector2(240, 60)), 32, TextAlignmentOptions.Right).text = "Логин";
            _login = MakeInput(panel, new Vector2(110, 100), new Vector2(600, 72), _s.Login);
            AddText(Box(panel, "PasswordLabel", new Vector2(-330, 15), new Vector2(240, 60)), 32, TextAlignmentOptions.Right).text = "Пароль";
            _password = MakeInput(panel, new Vector2(110, 15), new Vector2(600, 72), "");
            _password.contentType = TMP_InputField.ContentType.Password;
            MakeButton(panel, "Войти", new Vector2(-170, -90), new Vector2(400, 96),
                () => _s.Connect(_login.text, _password.text, register: false), Magenta);
            MakeButton(panel, "Регистрация", new Vector2(250, -90), new Vector2(400, 96),
                () => _s.Connect(_login.text, _password.text, register: true), Grey);

            ShowLocation(0);
        }

        /// <summary>Показывает карту локации: фон и объекты-столы. Закрытые столы — замок, открытые — кнопка quick_join.</summary>
        void ShowLocation(int idx)
        {
            if (_locations == null || _locations.Length == 0) return;
            _location = Mathf.Clamp(idx, 0, Mathf.Min(_locations.Length, TableRects.Length) - 1);
            var art = _locations[_location];
            if (art.Background != null && !_blurred.ContainsKey(art.Background)) _blurred[art.Background] = MakeBlurred(art.Background);
            foreach (var (rt, sprite) in new[] { (_mapBackdrop, art.Background != null ? _blurred[art.Background] : null), (_map, art.Background) })
            {
                var img = rt.GetComponent<Image>();
                img.sprite = sprite;
                img.color = sprite != null ? Color.white : Night;
                if (art.Background != null) rt.GetComponent<AspectRatioFitter>().aspectRatio = art.Background.rect.width / art.Background.rect.height;
            }

            foreach (Transform c in _tables) Destroy(c.gameObject);
            _tableButtons.Clear();
            var rects = TableRects[_location];
            for (var i = 0; i < rects.Length; i++)
            {
                var r = rects[i];
                var level = TableLevels[_location][i];
                var locked = level > _playerLevel;
                // якоря в долях фона: объект масштабируется вместе с картой
                var spot = new GameObject($"Table{i + 1}", typeof(RectTransform)).GetComponent<RectTransform>();
                spot.SetParent(_tables, false);
                spot.anchorMin = new Vector2(r.xMin, 1f - r.yMax);
                spot.anchorMax = new Vector2(r.xMax, 1f - r.yMin);
                spot.offsetMin = spot.offsetMax = Vector2.zero;
                spot.gameObject.AddComponent<Image>().color = Color.clear; // прозрачная зона клика
                // ponytail: серые *-inactive спрайты не накладываем — их точные координаты на фоне неизвестны, объект двоится

                if (locked)
                {
                    var lockRt = Box(spot, "Lock", Vector2.zero, new Vector2(170, 119));
                    MakeImage(lockRt, _lockPanel, PanelActive);
                    AddText(Stretch(lockRt, "Text"), 19, TextAlignmentOptions.Center, Cyan).text = $"СТОЛ {i + 1}\n\n\nУРОВЕНЬ {level}";
                    continue;
                }

                var name = Box(spot, "Name", new Vector2(0, 40), new Vector2(240, 56));
                name.pivot = new Vector2(0.5f, 0f);
                MakeImage(name, At(art.Names, i), Color.clear).preserveAspect = true;
                var panelImg = MakeImage(Box(spot, "Panel", Vector2.zero, new Vector2(210, 71)), _activePanel, PanelActive);
                var stake = TableStakes[Mathf.Min(i, TableStakes.Length - 1)];
                var button = spot.gameObject.AddComponent<Button>();
                button.targetGraphic = panelImg;
                button.onClick.AddListener(() => _s.QuickJoin(stake));
                button.interactable = _s.State == ClientState.Authorized;
                _tableButtons.Add(button);
            }

            _prevLoc.gameObject.SetActive(_location > 0);
            _nextLoc.gameObject.SetActive(_location < Mathf.Min(_locations.Length, TableRects.Length) - 1);
            _nextLoc.interactable = _location + 1 < _unlockedLocations;
        }

        static Sprite At(Sprite[] a, int i) => a != null && i < a.Length ? a[i] : null;
        static string Chips(long n) => n.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));

        void BuildTable(Transform root)
        {
            _table = Stretch(root, "Table").gameObject;
            var t = _table.transform;
            // рендер стола целиком по высоте экрана, чтобы все кресла были видны; боковые поля закрывает та же картинка растянутой подложкой
            MakeCover(t, _tableBackground, Night);
            MakePanel(Stretch(t, "Dim"), new Color(0, 0, 0, 0.45f));
            var art = Stretch(t, "TableArt");
            MakeImage(art, _tableBackground, Color.clear);
            var fit = art.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectRatio = _tableBackground != null ? _tableBackground.rect.width / _tableBackground.rect.height : 16f / 9f;
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            _header = AddText(Box(t, "Header", new Vector2(30, -30), new Vector2(1400, 50), new Vector2(0f, 1f)), 28, TextAlignmentOptions.Left);
            MakeButton(t, "Встать", new Vector2(-40, -70), new Vector2(240, 80), _s.LeaveTable, Grey, new Vector2(1f, 1f));

            // по вертикали холст может ужаться до ~960 единиц (масштаб по ширине), низ считаем от нижнего края
            var center = Box(t, "Center", new Vector2(0, 125), new Vector2(620, 220));
            MakePanel(center, Panel);
            var cardSize = new Vector2(96, 134);
            for (var i = 0; i < _board.Length; i++)
                _board[i] = MakeCard(center, new Vector2(-232 + i * 116, 35), cardSize);
            _pot = AddText(Box(center, "Pot", new Vector2(0, -55), new Vector2(600, 56)), 40, TextAlignmentOptions.Center, Gold);
            _status = AddText(Box(center, "Status", new Vector2(0, -90), new Vector2(600, 40)), 26, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.8f));

            for (var i = 0; i < TableModel.MaxSeats; i++)
            {
                // своё место (0) привязано к нижнему краю, чтобы на любом экране не наезжать на карты и центр
                var seat = i == 0
                    ? Box(t, "Seat0", new Vector2(0, 375), new Vector2(340, 120), new Vector2(0.5f, 0f))
                    : Box(art, $"Seat{i}", Vector2.zero, new Vector2(300, 130), new Vector2(SeatUV[i].x, 1f - SeatUV[i].y));
                seat.pivot = new Vector2(0.5f, 0.5f);
                _seatRt[i] = seat;
                _seatBg[i] = MakePanel(seat, Panel);
                _seatText[i] = AddText(Stretch(seat, "Text"), 26, TextAlignmentOptions.Center);
                var small = new Vector2(48, 68);
                _seatCards[i] = new[]
                {
                    MakeCard(seat, new Vector2(-30, 95), small),
                    MakeCard(seat, new Vector2(30, 95), small),
                };
            }
            _timer = AddText(Box(t, "Timer", new Vector2(-12, -6), new Vector2(80, 44), new Vector2(1f, 1f)), 34, TextAlignmentOptions.Right, Gold);

            var mine = new Vector2(120, 168);
            _myCards[0] = MakeCard(t, new Vector2(-66, 205), mine, new Vector2(0.5f, 0f));
            _myCards[1] = MakeCard(t, new Vector2(66, 205), mine, new Vector2(0.5f, 0f));

            _sliderRow = Box(t, "SliderRow", new Vector2(0, 150), new Vector2(1000, 60), new Vector2(0.5f, 0f)).gameObject;
            _sliderLabel = AddText(Box(_sliderRow.transform, "Label", new Vector2(-380, 0), new Vector2(300, 60)), 32, TextAlignmentOptions.Right, Gold);
            _slider = MakeSlider(_sliderRow.transform, new Vector2(150, 0), new Vector2(600, 40));
            _slider.onValueChanged.AddListener(v => _sliderLabel.text = $"{_sliderPrefix}: {(long)v}");

            _actions = Box(t, "Actions", new Vector2(0, 55), new Vector2(1400, 100), new Vector2(0.5f, 0f));
        }

        // ------------------------------------------------------------------ элементы
        static RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            rt.anchorMin = rt.anchorMax = rt.pivot = a;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Скруглённая панель; без спрайта (до Start) — обычный прямоугольник.</summary>
        Image MakePanel(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        static Image MakeImage(RectTransform rt, Sprite sprite, Color fallback)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = sprite != null ? Color.white : fallback;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Картинка на весь экран с обрезкой по краям (как CSS background-size: cover).</summary>
        static RectTransform MakeCover(Transform parent, Sprite sprite, Color fallback)
        {
            var rt = Stretch(parent, "Cover");
            MakeImage(rt, sprite, fallback);
            var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectRatio = sprite != null ? sprite.rect.width / sprite.rect.height : 16f / 9f;
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            return rt;
        }

        TMP_Text AddText(RectTransform rt, float size, TextAlignmentOptions align, Color? color = null)
        {
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color ?? Color.white;
            text.richText = true;
            text.raycastTarget = false;
            return text;
        }

        Button SpriteButton(Transform parent, string name, Vector2 pos, Vector2 size, Vector2 anchor, Sprite sprite, UnityAction onClick)
        {
            var rt = Box(parent, name, pos, size, anchor);
            var img = MakeImage(rt, sprite, Panel);
            img.preserveAspect = true;
            img.raycastTarget = true; // MakeImage выключает клики: картинки декоративные, а это кнопка
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
            return button;
        }

        Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, UnityAction onClick, Color? color = null, Vector2? anchor = null)
        {
            var rt = Box(parent, "Button " + label, pos, size, anchor);
            var img = MakePanel(rt, color ?? Cyan);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
            AddText(Stretch(rt, "Label"), 34, TextAlignmentOptions.Center).text = label;
            return button;
        }

        TMP_InputField MakeInput(Transform parent, Vector2 pos, Vector2 size, string value)
        {
            var rt = Box(parent, "Input", pos, size);
            var img = MakePanel(rt, new Color(1f, 1f, 1f, 0.92f));
            var area = Stretch(rt, "TextArea");
            area.offsetMin = new Vector2(20, 8);
            area.offsetMax = new Vector2(-20, -8);
            area.gameObject.AddComponent<RectMask2D>();
            var text = AddText(Stretch(area, "Text"), 34, TextAlignmentOptions.Left, Color.black);
            var input = rt.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = img;
            input.textViewport = area;
            input.textComponent = text;
            input.fontAsset = _font;
            input.pointSize = 34;
            input.text = value;
            return input;
        }

        Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size)
        {
            var rt = Box(parent, "Slider", pos, size);
            MakePanel(rt, Panel);
            var handleArea = Stretch(rt, "HandleArea");
            handleArea.offsetMin = new Vector2(20, 0);
            handleArea.offsetMax = new Vector2(-20, 0);
            var handle = Box(handleArea, "Handle", Vector2.zero, new Vector2(40, 70));
            var handleImg = MakePanel(handle, Gold);
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.handleRect = handle;
            slider.wholeNumbers = true;
            return slider;
        }

        /// <summary>Белый квадрат со скруглёнными углами под 9-slice: один спрайт на все панели, кнопки и карты.</summary>
        /// <summary>Сильно размытая копия спрайта: уменьшаем вдвое на GPU до ~50 px (текстуры нечитаемые и без мипмапов),
        /// обратно растягивает билинейный фильтр. Для подложки под карту, где детали только мешают.</summary>
        static Sprite MakeBlurred(Sprite src)
        {
            var cur = RenderTexture.GetTemporary(src.texture.width, src.texture.height, 0);
            Graphics.Blit(src.texture, cur);
            while (cur.width > 64)
            {
                var next = RenderTexture.GetTemporary(cur.width / 2, cur.height / 2, 0);
                Graphics.Blit(cur, next);
                RenderTexture.ReleaseTemporary(cur);
                cur = next;
            }
            var prev = RenderTexture.active;
            RenderTexture.active = cur;
            var tex = new Texture2D(cur.width, cur.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, cur.width, cur.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(cur);
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        static Sprite MakeRoundedSprite(int radius)
        {
            var size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // расстояние от пикселя до ближайшего центра скругления; внутри — 1, на кромке — сглаживание
                var dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                var dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                var a = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }
}
