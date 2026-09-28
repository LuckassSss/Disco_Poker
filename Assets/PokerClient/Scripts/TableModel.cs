using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace PokerClient
{
    public sealed class SeatModel
    {
        public int SeatNo;
        public string PlayerId, Nickname;
        public string State = "", LastAction = "";
        public long Stack, Bet;
        public bool Connected = true, SittingOut, HasCards;
        public List<string> ShownCards;

        public bool Occupied => !string.IsNullOrEmpty(PlayerId);

        public void Set(JObject s)
        {
            PlayerId = s.Value<string>("player_id");
            Nickname = s.Value<string>("nickname");
            Stack = s.Value<long?>("stack") ?? 0;
            Bet = s.Value<long?>("bet") ?? 0;
            State = s.Value<string>("state") ?? "";
            Connected = s.Value<bool?>("connected") ?? true;
            SittingOut = s.Value<bool?>("sitting_out") ?? false;
            HasCards = s.Value<bool?>("has_cards") ?? false;
            if (!Occupied) Clear();
        }

        public void Clear()
        {
            PlayerId = Nickname = null;
            Stack = Bet = 0;
            State = LastAction = "";
            Connected = true;
            SittingOut = HasCards = false;
            ShownCards = null;
        }
    }

    public sealed class BetOption
    {
        public string Action;
        public long Min, Max;
    }

    /// <summary>
    /// Единственное место состояния стола. Обработчики сообщений правят модель,
    /// UI только читает её и перерисовывается, когда меняется <see cref="Version"/>.
    /// Правил покера здесь нет: всё, что показано, пришло от сервера.
    /// </summary>
    public sealed class TableModel
    {
        public const int MaxSeats = 5;

        public readonly SeatModel[] Seats = new SeatModel[MaxSeats];
        public string TableId;
        public string TableState = "", Stake = "";
        public int ButtonSeat = -1, HandNo;

        public string HandId;
        public string Street = "";
        public readonly List<string> Board = new();
        public long Pot;
        public int ToAct = -1;
        public double DeadlineTs;

        public int MySeat = -1;
        public readonly List<string> HoleCards = new();
        public readonly List<BetOption> Options = new();
        /// <summary>Порядок мест из <c>cards_dealt</c> — только для анимации; <see cref="DealNo"/> растёт с каждой раздачей.</summary>
        public readonly List<int> DealOrder = new();
        public int DealNo;
        public long Balance;

        /// <summary>Последнее событие словами — для строки статуса.</summary>
        public string Status = "";
        /// <summary>Растёт при каждом изменении; UI сравнивает и перерисовывается.</summary>
        public int Version;

        public bool AtTable => TableId != null;
        public bool MyTurn => Options.Count > 0;

        public TableModel()
        {
            for (var i = 0; i < MaxSeats; i++) Seats[i] = new SeatModel { SeatNo = i };
        }

        /// <summary>Серверное место → экранная позиция: 0 — своё (снизу), дальше по часовой.</summary>
        public static int ScreenPos(int seatNo, int mySeat)
        {
            if (mySeat < 0) return seatNo;
            return ((seatNo - mySeat) % MaxSeats + MaxSeats) % MaxSeats;
        }

        public void Clear()
        {
            foreach (var s in Seats) s.Clear();
            TableId = null;
            TableState = Stake = Street = "";
            ButtonSeat = ToAct = MySeat = -1;
            HandNo = 0;
            HandId = null;
            Board.Clear();
            HoleCards.Clear();
            Options.Clear();
            Pot = 0;
            DeadlineTs = 0;
            Version++;
        }

        public void ApplySnapshot(JObject d)
        {
            var table = d["table"] as JObject;
            if (table != null)
            {
                TableId = table.Value<string>("table_id");
                TableState = table.Value<string>("state") ?? "";
                ButtonSeat = table.Value<int?>("button_seat") ?? -1;
                HandNo = table.Value<int?>("hand_no") ?? 0;
                Stake = (table["stake"] as JObject)?.Value<string>("code") ?? "";
            }

            foreach (var s in Seats) s.Clear();
            if (d["seats"] is JArray seats)
                foreach (var s in seats)
                    if (s is JObject so) SeatAt(so)?.Set(so);

            Board.Clear();
            Pot = 0;
            ToAct = -1;
            HandId = null;
            Street = "";
            DeadlineTs = 0;
            if (d["hand"] is JObject hand)
            {
                HandId = hand.Value<string>("hand_id");
                Street = hand.Value<string>("street") ?? "";
                SetBoard(hand["board"]);
                Pot = hand.Value<long?>("pot") ?? 0;
                ToAct = hand.Value<int?>("to_act") ?? -1;
                var deadlineIn = hand.Value<double?>("deadline_in");
                var serverTime = d.Value<double?>("server_time");
                DeadlineTs = deadlineIn.HasValue && serverTime.HasValue ? serverTime.Value + deadlineIn.Value : 0;
            }

            HoleCards.Clear();
            Options.Clear();
            MySeat = -1;
            if (d["you"] is JObject you)
            {
                MySeat = you.Value<int?>("seat_no") ?? -1;
                SetCards(HoleCards, you["hole_cards"]);
                SetOptions(you["options"]);
                Balance = you.Value<long?>("balance") ?? Balance;
            }
            Version++;
        }

        /// <summary>Применяет игровое сообщение. Неизвестный тип — не ошибка, вернёт false.</summary>
        public bool Apply(string t, JObject d)
        {
            switch (t)
            {
                case "joined_table":
                case "table_snapshot":
                    ApplySnapshot(d);
                    return true;

                case "hand_started":
                    HandId = d.Value<string>("hand_id");
                    ButtonSeat = d.Value<int?>("button_seat") ?? ButtonSeat;
                    Street = "preflop";
                    Board.Clear();
                    Pot = 0;
                    ToAct = -1;
                    HoleCards.Clear();
                    Options.Clear();
                    foreach (var s in Seats)
                    {
                        s.Bet = 0;
                        s.LastAction = "";
                        s.ShownCards = null;
                        s.HasCards = false;
                    }
                    if (d["players"] is JArray players)
                        foreach (var p in players)
                            if (p is JObject po && SeatAt(po) is { } seat)
                            {
                                seat.Stack = po.Value<long?>("stack") ?? seat.Stack;
                                seat.HasCards = true;
                                seat.State = "in_hand"; // иначе «folded» с прошлой раздачи прячет карты
                            }
                    HandNo++;
                    Status = "Новая раздача";
                    break;

                case "blind_posted":
                    if (SeatAt(d) is { } blindSeat)
                    {
                        blindSeat.Bet = d.Value<long?>("amount") ?? 0;
                        blindSeat.Stack = d.Value<long?>("stack") ?? blindSeat.Stack;
                        blindSeat.LastAction = d.Value<string>("kind") == "small_blind" ? "SB" : "BB";
                    }
                    Pot = d.Value<long?>("pot") ?? Pot;
                    break;

                case "hole_cards":
                    SetCards(HoleCards, d["cards"]);
                    break;

                case "cards_dealt":
                    DealOrder.Clear();
                    if (d["seats"] is JArray dealt)
                        foreach (var n in dealt)
                            if (SeatAt(n.Value<int>()) is { } s)
                            {
                                s.HasCards = true;
                                DealOrder.Add(s.SeatNo);
                            }
                    DealNo++;
                    break;

                case "action_required":
                    ToAct = d.Value<int?>("seat_no") ?? -1;
                    HandId = d.Value<string>("hand_id") ?? HandId;
                    Street = d.Value<string>("street") ?? Street;
                    Pot = d.Value<long?>("pot") ?? Pot;
                    DeadlineTs = d.Value<double?>("deadline_ts") ?? 0;
                    SetOptions(d["options"]); // нет options — ход чужой, кнопок нет
                    break;

                case "player_action":
                    if (SeatAt(d) is { } actor)
                    {
                        actor.Stack = d.Value<long?>("stack") ?? actor.Stack;
                        actor.Bet = d.Value<long?>("bet") ?? actor.Bet;
                        actor.State = d.Value<string>("state") ?? actor.State;
                        var action = d.Value<string>("action") ?? "";
                        var amount = d.Value<long?>("amount") ?? 0;
                        actor.LastAction = amount > 0 && action != "fold" && action != "check" ? $"{action} {amount}" : action;
                        if (actor.SeatNo == MySeat) Options.Clear();
                    }
                    Pot = d.Value<long?>("pot") ?? Pot;
                    ToAct = -1;
                    Status = "";
                    break;

                case "action_timeout":
                    if (SeatAt(d) is { } late) late.LastAction = "время вышло";
                    break;

                case "street":
                    Street = d.Value<string>("street") ?? Street;
                    SetBoard(d["board"]); // борд целиком, не накапливаем
                    Pot = d.Value<long?>("pot") ?? Pot;
                    foreach (var s in Seats)
                    {
                        s.Bet = 0;
                        if (s.State != "folded") s.LastAction = "";
                    }
                    break;

                case "uncalled_bet_returned":
                    if (SeatAt(d) is { } back)
                    {
                        back.Stack = d.Value<long?>("stack") ?? back.Stack;
                        back.Bet = 0;
                    }
                    break;

                case "showdown":
                    if (d["hands"] is JArray hands)
                        foreach (var h in hands)
                            if (h is JObject ho && SeatAt(ho) is { } shown)
                            {
                                shown.ShownCards = new List<string>();
                                SetCards(shown.ShownCards, ho["cards"]);
                                shown.LastAction = ho.Value<string>("combination") ?? "";
                            }
                    break;

                case "hand_finished":
                    SetBoard(d["board"]);
                    Pot = d.Value<long?>("total_pot") ?? Pot;
                    ToAct = -1;
                    Options.Clear();
                    if (d["stacks"] is JObject stacks)
                        foreach (var kv in stacks)
                            if (int.TryParse(kv.Key, out var no) && SeatAt(no) is { } s)
                                s.Stack = kv.Value.Value<long>();
                    var names = new List<string>();
                    if (d["winners"] is JArray winners)
                        foreach (var w in winners)
                            if (w is JObject wo && SeatAt(wo) is { } win)
                            {
                                var amount = wo.Value<long?>("amount") ?? 0;
                                win.LastAction = $"+{amount}";
                                names.Add($"{win.Nickname} +{amount}");
                            }
                    Status = names.Count > 0 ? "Выиграл: " + string.Join(", ", names) : "Раздача окончена";
                    break;

                case "seat_update":
                case "player_joined":
                    if (d["seat"] is JObject seatObj) SeatAt(seatObj)?.Set(seatObj);
                    break;

                case "player_left":
                    SeatAt(d)?.Clear();
                    break;

                case "table_state":
                    TableState = d.Value<string>("state") ?? TableState;
                    break;

                case "chat":
                    Status = $"{d.Value<string>("nickname")}: {d.Value<string>("text")}";
                    break;

                default:
                    return false;
            }
            Version++;
            return true;
        }

        SeatModel SeatAt(JObject o) => SeatAt(o.Value<int?>("seat_no") ?? -1);
        SeatModel SeatAt(int no) => no >= 0 && no < MaxSeats ? Seats[no] : null;

        void SetBoard(JToken cards)
        {
            Board.Clear();
            SetCards(Board, cards);
        }

        static void SetCards(List<string> target, JToken cards)
        {
            if (cards is not JArray arr) return;
            foreach (var c in arr) target.Add(c.Value<string>());
        }

        void SetOptions(JToken options)
        {
            Options.Clear();
            if (options is not JArray arr) return;
            foreach (var o in arr)
                Options.Add(new BetOption
                {
                    Action = o.Value<string>("action"),
                    Min = o.Value<long?>("min_amount") ?? 0,
                    Max = o.Value<long?>("max_amount") ?? 0,
                });
        }
    }
}
