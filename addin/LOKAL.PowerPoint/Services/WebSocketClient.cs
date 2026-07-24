using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LOKAL.PowerPoint
{
    /// <summary>
    /// WebSocket client for real-time communication with the LOKAL backend.
    /// Receives student join/response events and broadcasts activity state.
    /// </summary>
    public class WebSocketClient
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _room;

        public event Action<WsMessage> OnMessage;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<Response> OnStudentResponse;
        public event Action<Participant> OnStudentJoin;

        public bool IsConnected => _ws?.State == WebSocketState.Open;

        public async Task ConnectAsync(string serverUrl, string room, string teacherToken)
        {
            // Fully detach the old class room before installing a new socket.
            Disconnect();

            _room = room;
            var cts = new CancellationTokenSource();
            var socket = new ClientWebSocket();
            _cts = cts;
            _ws = socket;

            var wsUrl = serverUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            wsUrl = $"{wsUrl}/ws?room={Uri.EscapeDataString(room)}&role=teacher&id=teacher" +
                $"&token={Uri.EscapeDataString(teacherToken ?? string.Empty)}";

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.ConnectAsync(new Uri(wsUrl), timeoutCts.Token);
            }

            OnConnected?.Invoke();

            // Capture this connection. A later reconnect must not make an older
            // receive loop read from the replacement _ws/_cts fields.
            _ = ReceiveLoop(socket, cts.Token);
        }

        public void Disconnect()
        {
            var cts = _cts;
            var socket = _ws;
            _cts = null;
            _ws = null;
            try
            {
                cts?.Cancel();
                if (socket?.State == WebSocketState.Open)
                {
                    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing",
                        CancellationToken.None).Wait(2000);
                }
            }
            catch { }
            finally
            {
                socket?.Dispose();
                cts?.Dispose();
            }
        }

        public async Task SendAsync(WsMessage message)
        {
            var socket = _ws;
            var cts = _cts;
            if (socket?.State != WebSocketState.Open || cts == null) return;

            var json = JsonConvert.SerializeObject(message, LokalJson.Settings);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text, true, cts.Token);
        }

        private async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        try
                        {
                            var msg = JsonConvert.DeserializeObject<WsMessage>(json);
                            HandleMessage(msg);
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
            finally
            {
                OnDisconnected?.Invoke();
            }
        }

        private void HandleMessage(WsMessage msg)
        {
            if (msg == null) return;
            OnMessage?.Invoke(msg);

            switch (msg.Type)
            {
                case "response":
                    var response = JsonConvert.DeserializeObject<Response>(
                        msg.Payload?.ToString(), LokalJson.Settings);
                    if (response != null)
                        OnStudentResponse?.Invoke(response);
                    break;

                case "student_join":
                    try
                    {
                        dynamic payload = JsonConvert.DeserializeObject<dynamic>(
                            msg.Payload?.ToString());
                        var participant = JsonConvert.DeserializeObject<Participant>(
                            payload.participant?.ToString(), LokalJson.Settings);
                        if (participant != null)
                            OnStudentJoin?.Invoke(participant);
                    }
                    catch { }
                    break;
            }
        }
    }
}
