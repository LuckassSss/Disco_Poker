using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PokerClient
{
    /// <summary>
    /// Транспорт: только сокет. Приём в фоновом потоке, наружу — через очередь,
    /// которую разбирает главный поток (<see cref="TryDequeue"/>). Про покер не знает.
    /// </summary>
    public sealed class WsTransport : IDisposable
    {
        readonly ConcurrentQueue<string> _inbox = new();
        readonly SemaphoreSlim _sendLock = new(1, 1);
        ClientWebSocket _ws;
        CancellationTokenSource _cts;

        public volatile bool Closed;
        /// <summary>Код закрытия от сервера (4401, 4001, 1000…); 0, если соединение просто оборвалось.</summary>
        public int CloseCode;
        public string CloseReason;

        public async Task ConnectAsync(string url)
        {
            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            // Mono игнорирует токен отмены внутри ConnectAsync: при пропавшей сети он висит минутами,
            // и реконнект никогда не повторяется — поэтому ждём с таймаутом и рвём сокет сами
            var connect = _ws.ConnectAsync(new Uri(url), _cts.Token);
            if (await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(10))) != connect)
            {
                _ws.Abort();
                throw new TimeoutException("нет ответа от сервера за 10 с");
            }
            await connect; // пробросит ошибку подключения, если она была
            _ = Task.Run(ReceiveLoop);
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[16 * 1024];
            var message = new MemoryStream();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        CloseCode = _ws.CloseStatus.HasValue ? (int)_ws.CloseStatus.Value : 0;
                        CloseReason = _ws.CloseStatusDescription;
                        break;
                    }
                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    if (r.MessageType == WebSocketMessageType.Text)
                        _inbox.Enqueue(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
                    message.SetLength(0);
                }
            }
            catch (Exception e)
            {
                CloseReason ??= e.Message;
            }
            finally
            {
                Closed = true;
            }
        }

        public bool TryDequeue(out string text) => _inbox.TryDequeue(out text);

        public bool IsOpen => _ws != null && _ws.State == WebSocketState.Open;

        public async Task SendAsync(string text)
        {
            if (!IsOpen) return; // сокет ещё открывается или уже закрыт
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync();
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _ws?.Dispose();
            Closed = true;
        }
    }
}
