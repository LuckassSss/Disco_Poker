using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PokerClient
{
    public enum ClientState { Disconnected, Connecting, Authorized, Joining, AtTable, Reconnecting }

    /// <summary>
    /// Сессия с сервером: конечный автомат, гостевая авторизация, разбор конвертов
    /// {"t","d","rid"}, пинг и реконнект. Игровые сообщения отдаёт в <see cref="TableModel"/>.
    /// </summary>
    public sealed class PokerSession : MonoBehaviour
    {
        const string TokenKey = "poker.token";
        const string HostKey = "poker.host";
        const string NickKey = "poker.nick";
        const float PingInterval = 12f;
        static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

        public readonly TableModel Model = new();
        public ClientState State { get; private set; }
        /// <summary>Что показать игроку: ошибка, статус соединения. Пусто — всё хорошо.</summary>
        public string Message { get; private set; } = "";
        public string Host { get; private set; }
        public string Nickname { get; private set; }
        /// <summary>Ход отправлен, ждём ответа сервера — кнопки заблокированы.</summary>
        public bool ActionPending { get; private set; }

        WsTransport _ws;
        double _clockOffset; // server_time − Time.realtimeSinceStartupAsDouble
        float _nextPing;
        float _reconnectAt = float.MaxValue;
        int _reconnectAttempt;

        /// <summary>Серверное время сейчас, по поправке из welcome/pong. Локальные часы не используются.</summary>
        public double ServerNow => Time.realtimeSinceStartupAsDouble + _clockOffset;

        void Awake()
        {
            Application.runInBackground = true;
            Host = PlayerPrefs.GetString(HostKey, "192.168.0.43:8000");
            Nickname = PlayerPrefs.GetString(NickKey, "Игрок");
        }

        void OnDestroy() => _ws?.Dispose();

        // Тестовый хук: подключиться и сесть за стол без тапов — для iOS-симулятора без Simulator.app.
        // Запуск: SIMCTL_CHILD_POKER_AUTOJOIN=1 xcrun simctl launch <udid> com.discopoker.client (или аргумент -autojoin).
        bool _autoJoin;

        void Start()
        {
            _autoJoin = Environment.GetEnvironmentVariable("POKER_AUTOJOIN") == "1"
                        || Array.IndexOf(Environment.GetCommandLineArgs(), "-autojoin") >= 0;
            Debug.Log($"autojoin={_autoJoin} args=[{string.Join(" ", Environment.GetCommandLineArgs())}]");
            if (_autoJoin) Connect(Host, Nickname);
        }

        // ------------------------------------------------------------------ команды UI
        public async void Connect(string host, string nickname)
        {
            if (State != ClientState.Disconnected) return;
            Host = host.Trim();
            Nickname = string.IsNullOrWhiteSpace(nickname) ? "Игрок" : nickname.Trim();
            PlayerPrefs.SetString(HostKey, Host);
            PlayerPrefs.SetString(NickKey, Nickname);
            PlayerPrefs.Save();

            State = ClientState.Connecting;
            Message = "Получаю токен…";
            try
            {
                var token = await AuthAsync();
                await OpenSocketAsync(token);
            }
            catch (Exception e)
            {
                Fail("Не подключился: " + e.Message);
            }
        }

        public void Disconnect() => Fail("");

        public void QuickJoin(string stake)
        {
            if (State != ClientState.Authorized) return;
            State = ClientState.Joining;
            Message = "Ищу стол…";
            Send("quick_join", new JObject { ["stake"] = stake });
        }

        /// <summary>amount — суммарная ставка «до» на улице; для fold/check/call/all_in игнорируется.</summary>
        public void SendAction(string action, long amount)
        {
            if (State != ClientState.AtTable || !Model.MyTurn || ActionPending) return;
            ActionPending = true;
            Send("action", new JObject
            {
                ["hand_id"] = Model.HandId,
                ["action"] = action,
                ["amount"] = amount,
            });
        }

        public void LeaveTable() => Send("leave_table");
        public void SitIn() => Send("sit_in");

        // ------------------------------------------------------------------ подключение
        async Task<string> AuthAsync()
        {
            var saved = PlayerPrefs.GetString(TokenKey, "");
            var token = await PostGuestAsync(saved);
            if (token == null && saved != "") token = await PostGuestAsync(""); // старый токен протух — новая личность
            if (token == null) throw new Exception("сервер отверг гостевой вход");
            PlayerPrefs.SetString(TokenKey, token);
            PlayerPrefs.Save();
            return token;
        }

        async Task<string> PostGuestAsync(string oldToken)
        {
            var body = new JObject { ["nickname"] = Nickname };
            if (oldToken != "") body["token"] = oldToken;
            using var content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync($"http://{Host}/auth/guest", content);
            if (!resp.IsSuccessStatusCode) return null;
            return JObject.Parse(await resp.Content.ReadAsStringAsync()).Value<string>("token");
        }

        async Task OpenSocketAsync(string token)
        {
            _ws?.Dispose();
            _ws = new WsTransport();
            Message = "Подключаюсь…";
            await _ws.ConnectAsync($"ws://{Host}/ws?token={token}");
            _nextPing = Time.realtimeSinceStartup + PingInterval;
        }

        async Task ReconnectAsync()
        {
            try
            {
                await OpenSocketAsync(PlayerPrefs.GetString(TokenKey, ""));
            }
            catch (Exception)
            {
                ScheduleReconnect();
            }
        }

        void ScheduleReconnect()
        {
            State = ClientState.Reconnecting;
            // 1, 2, 4, 8 с, потолок 15, плюс разброс — чтобы шесть клиентов не стучались разом
            var delay = Mathf.Min(15f, Mathf.Pow(2f, _reconnectAttempt++)) * UnityEngine.Random.Range(0.8f, 1.2f);
            _reconnectAt = Time.realtimeSinceStartup + delay;
            Message = $"Связь потеряна, переподключаюсь через {delay:0} с…";
        }

        void Fail(string message)
        {
            _ws?.Dispose();
            _ws = null;
            State = ClientState.Disconnected;
            Message = message;
            Model.Clear();
            ActionPending = false;
            _reconnectAttempt = 0;
            _reconnectAt = float.MaxValue;
        }

        // ------------------------------------------------------------------ главный поток
        void Update()
        {
            if (_ws != null)
            {
                var closed = _ws.Closed; // читаем до разбора очереди: всё, что пришло до закрытия, уже в ней
                while (_ws.TryDequeue(out var raw)) Handle(raw);
                if (closed) OnClosed();
                else if (Time.realtimeSinceStartup >= _nextPing)
                {
                    Send("ping");
                    _nextPing = Time.realtimeSinceStartup + PingInterval;
                }
            }

            if (State == ClientState.Reconnecting && Time.realtimeSinceStartup >= _reconnectAt)
            {
                _reconnectAt = float.MaxValue;
                _ = ReconnectAsync();
            }
        }

        void OnClosed()
        {
            var code = _ws.CloseCode;
            var reason = _ws.CloseReason;
            _ws.Dispose();
            _ws = null;
            ActionPending = false;

            switch (code)
            {
                case 4401:
                    PlayerPrefs.DeleteKey(TokenKey);
                    Fail("Сессия истекла, подключитесь заново");
                    return;
                case 4001:
                    Fail("Вход выполнен с другого устройства");
                    return;
            }
            if (State == ClientState.Connecting)
            {
                Fail("Сервер закрыл соединение: " + reason);
                return;
            }
            ScheduleReconnect(); // модель не сбрасываем: место держится 120 с
        }

        void Send(string t, JObject d = null)
        {
            var ws = _ws;
            if (ws == null || ws.Closed) return;
            var envelope = new JObject { ["t"] = t, ["d"] = d ?? new JObject() };
            if (t != "ping") Debug.Log($"→ {envelope.ToString(Formatting.None)}");
            _ = ws.SendAsync(envelope.ToString(Formatting.None)).ContinueWith(
                task => Debug.LogWarning($"Не отправил {t}: {task.Exception?.GetBaseException().Message}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        // ------------------------------------------------------------------ диспетчер
        void Handle(string raw)
        {
            JObject envelope;
            try
            {
                envelope = JObject.Parse(raw);
            }
            catch (JsonException e)
            {
                Debug.LogWarning($"Битый JSON от сервера: {e.Message}");
                return;
            }

            var t = envelope.Value<string>("t");
            var d = envelope["d"] as JObject ?? new JObject();
            if (t != "pong") Debug.Log($"← {t} {(raw.Length > 300 ? raw.Substring(0, 300) + "…" : raw)}");

            switch (t)
            {
                case "welcome":
                    if (d.Value<bool?>("already_authorized") == true) return;
                    SyncClock(d);
                    Model.Balance = (d["player"] as JObject)?.Value<long?>("balance") ?? Model.Balance;
                    _reconnectAttempt = 0;
                    if (State == ClientState.Reconnecting && Model.AtTable)
                    {
                        // снимок придёт сам; если место уже отдали — сервер ответит not_at_table
                        Message = "Восстанавливаю стол…";
                        Send("request_snapshot");
                    }
                    else
                    {
                        State = ClientState.Authorized;
                        Message = "";
                        if (_autoJoin) { _autoJoin = false; QuickJoin("low"); }
                    }
                    return;

                case "pong":
                    SyncClock(d);
                    return;

                case "error":
                    OnError(d);
                    return;

                case "stakes":
                    return;

                case "joined_table":
                case "table_snapshot":
                    Model.ApplySnapshot(d);
                    State = ClientState.AtTable;
                    Message = "";
                    ActionPending = false;
                    return;

                case "left_table":
                    Model.Clear();
                    Model.Balance += d.Value<long?>("returned") ?? 0;
                    State = ClientState.Authorized;
                    Message = $"Вы встали из-за стола, на баланс вернулось {d.Value<long?>("returned") ?? 0}";
                    return;
            }

            if (!Model.Apply(t, d))
            {
                Debug.Log($"Неизвестное сообщение «{t}», игнорирую");
                return;
            }
            // сервер ответил на наш ход или снова просит ходить — кнопки можно разблокировать
            if (t == "player_action" && d.Value<int?>("seat_no") == Model.MySeat) ActionPending = false;
            if (t == "action_required" && Model.MyTurn) ActionPending = false;
        }

        void OnError(JObject d)
        {
            var code = d.Value<string>("code");
            Message = $"{code}: {d.Value<string>("message")}";
            ActionPending = false;
            switch (code)
            {
                case "not_at_table":
                    Model.Clear();
                    if (State is ClientState.AtTable or ClientState.Reconnecting) State = ClientState.Authorized;
                    break;
                case "invalid_action":
                case "not_your_turn":
                    Send("request_snapshot"); // расхождение лечится снимком, а не правкой UI
                    break;
                case "join_in_progress":
                    break; // ждём joined_table по первому запросу
                default:
                    if (State == ClientState.Joining) State = ClientState.Authorized;
                    break;
            }
        }

        void SyncClock(JObject d)
        {
            var serverTime = d.Value<double?>("server_time");
            if (serverTime.HasValue) _clockOffset = serverTime.Value - Time.realtimeSinceStartupAsDouble;
        }
    }
}
