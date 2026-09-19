using System.Collections.Generic;
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
        [SerializeField] Sprite _tableBackground;   // Locations/Location01/Backgrounds/without_UI
        [SerializeField] Sprite _tableObject;       // Locations/Location01/Objects/object-group-1-poker-table

        const float BallFps = 12f;

        // экранные позиции мест: 0 — своё (снизу, привязано к нижнему краю), дальше по часовой стрелке
        static readonly Vector2[] SeatPos =
        {
            Vector2.zero, new(-680, -120), new(-560, 230), new(0, 300), new(560, 230), new(680, -120),
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
        TMP_InputField _host, _nick;
        Button _lobbyButton;
        TMP_Text _lobbyButtonText, _lobbyMessage;

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
                _lobbyButton.interactable = st is ClientState.Disconnected or ClientState.Authorized;
                _lobbyButtonText.text = st == ClientState.Authorized ? "За стол" : "Подключиться";
                _host.interactable = _nick.interactable = st == ClientState.Disconnected;
                _lobbyMessage.text = st == ClientState.Authorized ? $"Баланс: {m.Balance}" : "";
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
            _message = AddText(Box(root, "Message", new Vector2(0, -4), new Vector2(1000, 40), new Vector2(0.5f, 1f)), 26, TextAlignmentOptions.Center, Gold);
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
            MakeCover(t, _loadingBackground, Night);
            if (_loadingBackground == null)
                AddText(Box(t, "Title", new Vector2(0, 300), new Vector2(1200, 120)), 96, TextAlignmentOptions.Center, Magenta).text = "DISCO POKER";

            var panel = Box(t, "Panel", new Vector2(0, 40), new Vector2(980, 360), new Vector2(0.5f, 0f));
            MakePanel(panel, Panel);
            AddText(Box(panel, "HostLabel", new Vector2(-330, 120), new Vector2(240, 60)), 32, TextAlignmentOptions.Right).text = "Сервер";
            _host = MakeInput(panel, new Vector2(110, 120), new Vector2(600, 72), _s.Host);
            AddText(Box(panel, "NickLabel", new Vector2(-330, 30), new Vector2(240, 60)), 32, TextAlignmentOptions.Right).text = "Ник";
            _nick = MakeInput(panel, new Vector2(110, 30), new Vector2(600, 72), _s.Nickname);
            _lobbyButton = MakeButton(panel, "Подключиться", new Vector2(-120, -85), new Vector2(440, 96), OnLobbyButton, Magenta);
            _lobbyButtonText = _lobbyButton.GetComponentInChildren<TMP_Text>();
            MakeButton(panel, "Отключиться", new Vector2(250, -85), new Vector2(260, 96), _s.Disconnect, Grey);
            _lobbyMessage = AddText(Box(panel, "LobbyMessage", new Vector2(0, -155), new Vector2(900, 40)), 28, TextAlignmentOptions.Center, Gold);
        }

        void BuildTable(Transform root)
        {
            _table = Stretch(root, "Table").gameObject;
            var t = _table.transform;
            MakeCover(t, _tableBackground, Night);
            MakePanel(Stretch(t, "Dim"), new Color(0, 0, 0, 0.45f));
            MakeImage(Box(t, "TableObject", new Vector2(0, 110), new Vector2(1000, 600)), _tableObject, Color.white).preserveAspect = true;

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
                    : Box(t, $"Seat{i}", SeatPos[i], new Vector2(300, 130));
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

        void OnLobbyButton()
        {
            if (_s.State == ClientState.Disconnected) _s.Connect(_host.text, _nick.text);
            else if (_s.State == ClientState.Authorized) _s.QuickJoin("low");
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
        static void MakeCover(Transform parent, Sprite sprite, Color fallback)
        {
            var rt = Stretch(parent, "Cover");
            MakeImage(rt, sprite, fallback);
            if (sprite == null) return;
            var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectRatio = sprite.rect.width / sprite.rect.height;
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
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
