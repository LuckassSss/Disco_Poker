using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace PokerClient.Tests
{
    public class TableModelTests
    {
        const string Snapshot = @"{
          ""table"": {""table_id"": ""low-a1b2"", ""stake"": {""code"": ""low""}, ""max_seats"": 5,
                      ""state"": ""hand_in_progress"", ""button_seat"": 2, ""hand_no"": 41},
          ""seats"": [
            {""seat_no"": 0, ""player_id"": ""p0"", ""nickname"": ""Женя"", ""stack"": 940, ""state"": ""in_hand"",
             ""bet"": 20, ""committed"": 20, ""connected"": true, ""sitting_out"": false, ""has_cards"": true},
            {""seat_no"": 1, ""player_id"": null, ""nickname"": null, ""stack"": 0, ""state"": ""empty"",
             ""bet"": 0, ""committed"": 0, ""connected"": false, ""sitting_out"": false, ""has_cards"": false},
            {""seat_no"": 3, ""player_id"": ""p3"", ""nickname"": ""Бот-3"", ""stack"": 500, ""state"": ""in_hand"",
             ""bet"": 0, ""committed"": 20, ""connected"": true, ""sitting_out"": false, ""has_cards"": true}
          ],
          ""hand"": {""hand_id"": ""h1"", ""street"": ""flop"", ""board"": [""As"", ""Kd"", ""7c""], ""pot"": 130,
                     ""current_bet"": 20, ""min_raise"": 20, ""to_act"": 3, ""deadline_in"": 12.4},
          ""you"": {""seat_no"": 0, ""stack"": 940, ""hole_cards"": [""Qh"", ""Qs""], ""balance"": 8600,
                    ""options"": [{""action"": ""fold""}, {""action"": ""call"", ""min_amount"": 40, ""max_amount"": 40}],
                    ""extra_field_from_future"": 1},
          ""server_time"": 1786000000.0
        }";

        [Test]
        public void Snapshot_ReplacesWholeState()
        {
            var m = new TableModel();
            m.Seats[4].PlayerId = "stale";

            m.ApplySnapshot(JObject.Parse(Snapshot));

            Assert.AreEqual("low-a1b2", m.TableId);
            Assert.AreEqual(0, m.MySeat);
            Assert.AreEqual(3, m.ToAct);
            Assert.AreEqual(130, m.Pot);
            Assert.AreEqual(new[] { "As", "Kd", "7c" }, m.Board);
            Assert.AreEqual(new[] { "Qh", "Qs" }, m.HoleCards);
            Assert.AreEqual(2, m.Options.Count);
            Assert.AreEqual(40, m.Options[1].Min);
            Assert.AreEqual(1786000012.4, m.DeadlineTs, 0.001);
            Assert.IsFalse(m.Seats[4].Occupied, "снимок — полное состояние, старое место должно исчезнуть");
            Assert.IsFalse(m.Seats[1].Occupied);
            Assert.AreEqual("Бот-3", m.Seats[3].Nickname);
        }

        [Test]
        public void ActionRequired_WithoutOptions_IsNotMyTurn()
        {
            var m = new TableModel();
            m.ApplySnapshot(JObject.Parse(Snapshot));

            m.Apply("action_required", JObject.Parse(@"{""seat_no"": 3, ""hand_id"": ""h1"", ""street"": ""flop"", ""pot"": 130, ""deadline_ts"": 1786000020.0}"));

            Assert.IsFalse(m.MyTurn);
            Assert.AreEqual(3, m.ToAct);
            Assert.AreEqual(1786000020.0, m.DeadlineTs, 0.001);
        }

        [Test]
        public void Street_ReplacesBoardEntirely()
        {
            var m = new TableModel();
            m.ApplySnapshot(JObject.Parse(Snapshot));

            m.Apply("street", JObject.Parse(@"{""street"": ""river"", ""cards"": [""2h""], ""board"": [""As"", ""Kd"", ""7c"", ""9d"", ""2h""], ""pot"": 300}"));

            Assert.AreEqual(5, m.Board.Count);
            Assert.AreEqual("river", m.Street);
            Assert.AreEqual(0, m.Seats[0].Bet, "ставки улицы обнуляются");
        }

        [Test]
        public void UnknownMessage_IsIgnoredWithoutChange()
        {
            var m = new TableModel();
            var version = m.Version;

            var handled = m.Apply("something_new", JObject.Parse(@"{""x"": 1}"));

            Assert.IsFalse(handled);
            Assert.AreEqual(version, m.Version);
        }

        [Test]
        public void CardsDealt_KeepsServerOrderForAnimation()
        {
            var m = new TableModel();

            m.Apply("cards_dealt", JObject.Parse(@"{""seats"": [3, 0, 7, 1], ""street"": ""preflop""}"));

            Assert.AreEqual(new[] { 3, 0, 1 }, m.DealOrder, "порядок сервера, мест вне стола нет");
            Assert.AreEqual(1, m.DealNo);
            Assert.IsTrue(m.Seats[3].HasCards);
        }

        [Test]
        public void HandStarted_ClearsFoldFromPreviousHand()
        {
            var m = new TableModel();
            m.Seats[0].State = "folded";

            m.Apply("hand_started", JObject.Parse(@"{""hand_id"": ""h2"", ""button_seat"": 3, ""players"": [{""seat_no"": 0, ""stack"": 900}]}"));

            Assert.AreEqual("in_hand", m.Seats[0].State);
        }

        [Test]
        public void HandFinished_MarksWinnerWithBestFive_UntilNextHand()
        {
            var m = new TableModel();
            m.Seats[1].PlayerId = "p1";
            m.Seats[3].PlayerId = "p3";

            m.Apply("showdown", JObject.Parse(@"{""hands"": [
                {""seat_no"": 1, ""cards"": [""Ah"", ""Kh""], ""combination"": ""Флеш"", ""best_five"": [""Ah"", ""Kh"", ""9h"", ""5h"", ""2h""]},
                {""seat_no"": 3, ""cards"": [""Qs"", ""Qd""], ""combination"": ""Пара"", ""best_five"": [""Qs"", ""Qd"", ""9h"", ""7c"", ""5h""]}]}"));
            m.Apply("hand_finished", JObject.Parse(@"{""total_pot"": 200, ""winners"": [{""seat_no"": 1, ""amount"": 200}]}"));

            Assert.IsTrue(m.Seats[1].Winner);
            Assert.IsFalse(m.Seats[3].Winner);
            Assert.AreEqual(new[] { "Ah", "Kh", "9h", "5h", "2h" }, m.Seats[1].BestFive);
            Assert.AreEqual(1, m.FinishNo);

            m.Apply("hand_started", JObject.Parse(@"{""hand_id"": ""h2"", ""players"": [{""seat_no"": 1}, {""seat_no"": 3}]}"));

            Assert.IsFalse(m.Seats[1].Winner);
            Assert.IsNull(m.Seats[1].BestFive);
        }

        [Test]
        public void ScreenPos_PutsMeAtBottomAndOthersClockwise()
        {
            Assert.AreEqual(0, TableModel.ScreenPos(4, 4));
            Assert.AreEqual(1, TableModel.ScreenPos(0, 4));
            Assert.AreEqual(2, TableModel.ScreenPos(1, 4));
            Assert.AreEqual(4, TableModel.ScreenPos(3, 4));
            Assert.AreEqual(2, TableModel.ScreenPos(2, -1), "без своего места — как есть");
        }
    }
}
