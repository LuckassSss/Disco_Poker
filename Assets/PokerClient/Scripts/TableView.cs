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
    /// Тестовый UI, собирается из кода: ни префабов, ни спрайтов карт (их пока нет в Art/Cards).
    /// Читает только <see cref="TableModel"/> и состояние <see cref="PokerSession"/>.
    /// ponytail: когда появятся спрайты и вёрстка от художника — заменить на префабы, логика чтения модели та же.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        // экранные позиции мест: 0 — своё (снизу), дальше по часовой стрелке
        static readonly Vector2[] SeatPos =
        {
            new(0, -140), new(-660, -130), new(-660, 210), new(0, 350), new(660, 210), new(660, -130),
        };
        static readonly Color Felt = new(0.07f, 0.33f, 0.19f);
        static readonly Color Panel = new(0, 0, 0, 0.55f);
        static readonly Color Active = new(0.85f, 0.65f, 0.1f, 0.85f);
        static readonly Color ButtonColor = new(0.2f, 0.45f, 0.8f);

        PokerSession _s;
        TMP_FontAsset _font;
        bool _suitGlyphs;

        GameObject _lobby, _table;
        TMP_InputField _host, _nick;
        Button _lobbyButton;
        TMP_Text _lobbyButtonText, _lobbyMessage;

        readonly TMP_Text[] _seatText = new TMP_Text[TableModel.MaxSeats];
        readonly RectTransform[] _seatRt = new RectTransform[TableModel.MaxSeats];
        readonly Image[] _seatBg = new Image[TableModel.MaxSeats];
        TMP_Text _header, _board, _pot, _status, _myCards, _timer, _message, _sliderLabel;
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
            _suitGlyphs = _font.HasCharacter('♠', false, true) && _font.HasCharacter('♥', false, true);
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
        }

        // ------------------------------------------------------------------ отрисовка
        void Redraw()
        {
            var m = _s.Model;
            var st = _s.State;
            var atTable = st == ClientState.AtTable || (st == ClientState.Reconnecting && m.AtTable);
            _lobby.SetActive(!atTable);
            _table.SetActive(atTable);
            _message.text = _s.Message;

            if (!atTable)
            {
                _lobbyButton.interactable = st is ClientState.Disconnected or ClientState.Authorized;
                _lobbyButtonText.text = st == ClientState.Authorized ? "За стол" : "Подключиться";
                _host.interactable = _nick.interactable = st == ClientState.Disconnected;
                _lobbyMessage.text = st == ClientState.Authorized ? $"Баланс: {m.Balance}" : "";
                return;
            }

            _header.text = $"Стол {m.TableId}   ставки: {m.Stake}   баланс: {m.Balance}";
            _board.text = m.Board.Count > 0 ? Cards(m.Board) : "<alpha=#60>—";
            _pot.text = $"Банк: {m.Pot}";
            _status.text = string.IsNullOrEmpty(m.Street) ? m.Status : $"{m.Street}   {m.Status}";
            _myCards.text = m.HoleCards.Count > 0 ? Cards(m.HoleCards) : "";

            for (var i = 0; i < TableModel.MaxSeats; i++)
            {
                var s = m.Seats[i];
                var pos = TableModel.ScreenPos(i, m.MySeat);
                var text = _seatText[pos];
                var bg = _seatBg[pos];
                if (!s.Occupied)
                {
                    text.text = "<alpha=#60>пусто";
                    bg.color = Panel;
                    continue;
                }
                var sb = new StringBuilder();
                sb.Append(i == m.MySeat ? "<b>" : "").Append(s.Nickname).Append(i == m.MySeat ? "</b>" : "");
                if (i == m.ButtonSeat) sb.Append("  <color=#FFD54F>D</color>");
                sb.Append('\n').Append(s.Stack);
                if (s.Bet > 0) sb.Append("   <color=#FFD54F>ставка ").Append(s.Bet).Append("</color>");
                sb.Append('\n');
                if (s.ShownCards != null) sb.Append(Cards(s.ShownCards));
                else if (s.HasCards && s.State != "folded" && i != m.MySeat) sb.Append("<alpha=#A0>□ □");
                sb.Append('\n');
                if (!s.Connected) sb.Append("<color=#FF8A80>оффлайн</color>");
                else if (s.SittingOut) sb.Append("<alpha=#A0>сит-аут");
                else sb.Append(s.LastAction);
                text.text = sb.ToString();
                var folded = s.State == "folded";
                text.alpha = folded ? 0.45f : 1f;
                bg.color = i == m.ToAct ? Active : Panel;
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
                UnityAction onClick;
                switch (o.Action)
                {
                    case "fold": label = "Фолд"; onClick = () => _s.SendAction("fold", 0); break;
                    case "check": label = "Чек"; onClick = () => _s.SendAction("check", 0); break;
                    case "call": label = $"Колл {o.Min}"; onClick = () => _s.SendAction("call", 0); break;
                    case "all_in": label = $"Олл-ин {o.Min}"; onClick = () => _s.SendAction("all_in", 0); break;
                    case "bet":
                    case "raise":
                        label = o.Action == "bet" ? "Бет" : "Рейз";
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
                var b = MakeButton(_actions, label, new Vector2(x, 0), new Vector2(280, 90), onClick);
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

        string Cards(List<string> cards)
        {
            var sb = new StringBuilder();
            foreach (var c in cards)
            {
                if (c.Length < 2) continue;
                var rank = c[0] == 'T' ? "10" : c[0].ToString();
                var suit = c[1];
                var red = suit == 'h' || suit == 'd';
                var glyph = _suitGlyphs
                    ? suit switch { 's' => "♠", 'h' => "♥", 'd' => "♦", 'c' => "♣", _ => "?" }
                    : suit.ToString();
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(red ? "<color=#FF6B6B>" : "<color=#FFFFFF>").Append(rank).Append(glyph).Append("</color>");
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ сборка UI
        void Build()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
                DontDestroyOnLoad(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)));

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            MakePanel(Stretch(root, "Background"), Felt);
            _message = AddText(Box(root, "Message", new Vector2(0, -4), new Vector2(1000, 40), new Vector2(0.5f, 1f)), 26, TextAlignmentOptions.Center, new Color(1f, 0.85f, 0.4f));

            BuildLobby(root);
            BuildTable(root);
        }

        void BuildLobby(Transform root)
        {
            _lobby = Stretch(root, "Lobby").gameObject;
            var t = _lobby.transform;
            AddText(Box(t, "Title", new Vector2(0, 260), new Vector2(1200, 120)), 96, TextAlignmentOptions.Center).text = "DISCO POKER";
            AddText(Box(t, "HostLabel", new Vector2(-560, 110), new Vector2(300, 60)), 32, TextAlignmentOptions.Right).text = "Сервер";
            _host = MakeInput(t, new Vector2(60, 110), new Vector2(800, 80), _s.Host);
            AddText(Box(t, "NickLabel", new Vector2(-560, 0), new Vector2(300, 60)), 32, TextAlignmentOptions.Right).text = "Ник";
            _nick = MakeInput(t, new Vector2(60, 0), new Vector2(800, 80), _s.Nickname);
            _lobbyButton = MakeButton(t, "Подключиться", new Vector2(0, -140), new Vector2(500, 110), OnLobbyButton);
            _lobbyButtonText = _lobbyButton.GetComponentInChildren<TMP_Text>();
            _lobbyMessage = AddText(Box(t, "LobbyMessage", new Vector2(0, -250), new Vector2(1200, 60)), 32, TextAlignmentOptions.Center);
            MakeButton(t, "Отключиться", new Vector2(0, -360), new Vector2(360, 80), _s.Disconnect, new Color(0.35f, 0.35f, 0.35f));
        }

        void BuildTable(Transform root)
        {
            _table = Stretch(root, "Table").gameObject;
            var t = _table.transform;

            _header = AddText(Box(t, "Header", new Vector2(30, -30), new Vector2(1400, 50), new Vector2(0f, 1f)), 28, TextAlignmentOptions.Left);
            MakeButton(t, "Встать", new Vector2(-40, -70), new Vector2(240, 80), _s.LeaveTable, new Color(0.6f, 0.25f, 0.25f), new Vector2(1f, 1f));

            // по вертикали холст может ужаться до ~960 единиц (масштаб по ширине), низ считаем от нижнего края
            MakePanel(Box(t, "Center", new Vector2(0, 120), new Vector2(760, 230)), new Color(0, 0, 0, 0.35f));
            _board = AddText(Box(t, "Board", new Vector2(0, 150), new Vector2(760, 120)), 72, TextAlignmentOptions.Center);
            _pot = AddText(Box(t, "Pot", new Vector2(0, 70), new Vector2(760, 60)), 40, TextAlignmentOptions.Center);
            _status = AddText(Box(t, "Status", new Vector2(0, 25), new Vector2(760, 50)), 28, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.8f));

            for (var i = 0; i < TableModel.MaxSeats; i++)
            {
                // своё место (0) привязано к нижнему краю, чтобы на любом экране не наезжать на карты и центр
                var seat = i == 0
                    ? Box(t, "Seat0", new Vector2(0, 305), new Vector2(340, 170), new Vector2(0.5f, 0f))
                    : Box(t, $"Seat{i}", SeatPos[i], new Vector2(340, 170));
                _seatRt[i] = seat;
                _seatBg[i] = MakePanel(seat, Panel);
                _seatText[i] = AddText(Stretch(seat, "Text"), 28, TextAlignmentOptions.Center);
            }
            _timer = AddText(Box(t, "Timer", new Vector2(-12, -6), new Vector2(80, 44), new Vector2(1f, 1f)), 34, TextAlignmentOptions.Right, new Color(1f, 0.85f, 0.4f));

            _myCards = AddText(Box(t, "MyCards", new Vector2(0, 250), new Vector2(600, 110), new Vector2(0.5f, 0f)), 80, TextAlignmentOptions.Center);

            _sliderRow = Box(t, "SliderRow", new Vector2(0, 160), new Vector2(1000, 70), new Vector2(0.5f, 0f)).gameObject;
            _sliderLabel = AddText(Box(_sliderRow.transform, "Label", new Vector2(-380, 0), new Vector2(300, 60)), 32, TextAlignmentOptions.Right);
            _slider = MakeSlider(_sliderRow.transform, new Vector2(150, 0), new Vector2(600, 40));
            _slider.onValueChanged.AddListener(v => _sliderLabel.text = $"{_sliderPrefix}: {(long)v}");

            _actions = Box(t, "Actions", new Vector2(0, 60), new Vector2(1400, 100), new Vector2(0.5f, 0f));
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

        static Image MakePanel(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
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
            var img = MakePanel(rt, color ?? ButtonColor);
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

        static Slider MakeSlider(Transform parent, Vector2 pos, Vector2 size)
        {
            var rt = Box(parent, "Slider", pos, size);
            MakePanel(rt, new Color(0, 0, 0, 0.5f));
            var handleArea = Stretch(rt, "HandleArea");
            handleArea.offsetMin = new Vector2(20, 0);
            handleArea.offsetMax = new Vector2(-20, 0);
            var handle = Box(handleArea, "Handle", Vector2.zero, new Vector2(40, 70));
            var handleImg = MakePanel(handle, new Color(1f, 0.85f, 0.4f));
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.targetGraphic = handleImg;
            slider.handleRect = handle;
            slider.wholeNumbers = true;
            return slider;
        }
    }
}
