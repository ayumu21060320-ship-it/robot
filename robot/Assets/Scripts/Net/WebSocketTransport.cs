#if HAS_NETCODE
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#else
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
#endif

/// <summary>
/// 自分のサーバー（server/server.js）を通して、部屋番号でつながる Netcode の通信方式。
///
/// - ホスト：サーバーに「部屋を作る」と送り、部屋番号をもらう。入ってきた人の番号ごとにデータを送り分ける
/// - 入る人：部屋番号を送って部屋に入り、データはすべてホストへ届く
/// - Offline = true のときはサーバーにつながず、1人用のホストとして動く
///
/// Web 版はブラウザの WebSocket（Plugins/WebGL/WebSocketBridge.jslib）、エディタと PC 版は .NET の ClientWebSocket を使う。
/// </summary>
public class WebSocketTransport : NetworkTransport
{
    /// <summary>サーバーの WebSocket の URL（例：wss://example.com/ws）</summary>
    public string ServerUrl = "ws://localhost:8080/ws";
    /// <summary>入る部屋の番号（入る人だけ）</summary>
    public string JoinCode = "";
    /// <summary>サーバーにつながず1人で遊ぶ</summary>
    public bool Offline;

    /// <summary>ホストがもらった部屋番号（もらうまでは空）</summary>
    public string RoomCode { get; private set; } = "";
    /// <summary>部屋に入れた（入る人）／部屋を作れた（ホスト）</summary>
    public bool Ready { get; private set; }
    /// <summary>うまくいかなかったときの理由</summary>
    public string LastError { get; private set; } = "";

    public override ulong ServerClientId => 0;

    bool m_IsServer, m_Running;
    IWsConnection m_Conn;
    readonly Queue<(NetworkEvent ev, ulong id, byte[] data)> m_Events = new Queue<(NetworkEvent, ulong, byte[])>();
    readonly HashSet<ulong> m_Peers = new HashSet<ulong>();
    bool m_SentHello, m_LostReported;

    public override void Initialize(NetworkManager networkManager = null) { }

    public override bool StartServer()
    {
        Reset(true);
        if (Offline) { Ready = true; return true; }
        return Connect();
    }

    public override bool StartClient()
    {
        Reset(false);
        if (Offline) { LastError = "1人用では参加できません。"; return false; }
        return Connect();
    }

    void Reset(bool server)
    {
        m_IsServer = server;
        m_Running = true;
        m_Events.Clear();
        m_Peers.Clear();
        m_SentHello = m_LostReported = false;
        RoomCode = LastError = "";
        Ready = false;
    }

    bool Connect()
    {
        if (string.IsNullOrEmpty(ServerUrl)) { LastError = "サーバーの URL がありません。"; return false; }
        try { m_Conn = WsConnections.Open(ServerUrl); }
        catch (Exception e) { LastError = "サーバーにつながりませんでした：" + e.Message; return false; }
        return true;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        if (m_Conn == null || !m_Conn.IsOpen) return;
        if (m_IsServer)
        {
            var buf = new byte[payload.Count + 4];
            uint id = (uint)clientId;
            buf[0] = (byte)id; buf[1] = (byte)(id >> 8); buf[2] = (byte)(id >> 16); buf[3] = (byte)(id >> 24);
            Buffer.BlockCopy(payload.Array, payload.Offset, buf, 4, payload.Count);
            m_Conn.Send(buf);
        }
        else
        {
            var buf = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array, payload.Offset, buf, 0, payload.Count);
            m_Conn.Send(buf);
        }
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        Pump();
        receiveTime = Time.realtimeSinceStartup;
        if (m_Events.Count > 0)
        {
            var (ev, id, data) = m_Events.Dequeue();
            clientId = id;
            payload = data != null ? new ArraySegment<byte>(data) : default;
            return ev;
        }
        clientId = 0;
        payload = default;
        return NetworkEvent.Nothing;
    }

    /// <summary>届いたメッセージを読んで、Netcode に渡すできごとに変える</summary>
    void Pump()
    {
        if (!m_Running || m_Conn == null) return;
        if (m_Conn.IsOpen && !m_SentHello)
        {
            m_SentHello = true;
            m_Conn.SendText(m_IsServer ? "{\"t\":\"create\"}" : "{\"t\":\"join\",\"code\":\"" + Escape(JoinCode) + "\"}");
        }
        while (m_Conn.TryReceive(out var data, out bool isText))
        {
            if (isText) HandleControl(Encoding.UTF8.GetString(data));
            else if (m_IsServer)
            {
                if (data.Length < 4) continue;
                ulong from = (ulong)(data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24));
                var body = new byte[data.Length - 4];
                Buffer.BlockCopy(data, 4, body, 0, body.Length);
                m_Events.Enqueue((NetworkEvent.Data, from, body));
            }
            else m_Events.Enqueue((NetworkEvent.Data, ServerClientId, data));
        }
        if (m_Conn.IsClosed && !m_LostReported)
        {
            m_LostReported = true;
            if (string.IsNullOrEmpty(LastError))
                LastError = Ready ? "サーバーとの接続が切れました。" : "サーバーにつながりませんでした（" + m_Conn.Error + "）。サーバーの URL と、サーバーが起動しているかを確かめてください。";
            if (m_IsServer)
            {
                foreach (var p in m_Peers) m_Events.Enqueue((NetworkEvent.Disconnect, p, null));
                m_Peers.Clear();
                m_Events.Enqueue((NetworkEvent.TransportFailure, ServerClientId, null));
            }
            else m_Events.Enqueue((NetworkEvent.Disconnect, ServerClientId, null));
        }
    }

    void HandleControl(string json)
    {
        string t = JsonField(json, "t");
        switch (t)
        {
            case "created":
                RoomCode = JsonField(json, "code");
                Ready = true;
                break;
            case "joined":
                Ready = true;
                m_Events.Enqueue((NetworkEvent.Connect, ServerClientId, null));
                break;
            case "peer":
                if (ulong.TryParse(JsonField(json, "id"), out var pid) && m_Peers.Add(pid))
                    m_Events.Enqueue((NetworkEvent.Connect, pid, null));
                break;
            case "left":
                if (ulong.TryParse(JsonField(json, "id"), out var lid) && m_Peers.Remove(lid))
                    m_Events.Enqueue((NetworkEvent.Disconnect, lid, null));
                break;
            case "error":
            case "closed":
                LastError = JsonField(json, "msg");
                m_Conn.Close();
                break;
        }
    }

    public override void DisconnectRemoteClient(ulong clientId)
    {
        if (m_Conn != null && m_Conn.IsOpen) m_Conn.SendText("{\"t\":\"kick\",\"id\":" + clientId + "}");
        m_Peers.Remove(clientId);
    }

    public override void DisconnectLocalClient() => Shutdown();

    public override ulong GetCurrentRtt(ulong clientId) => 0;

    public override void Shutdown()
    {
        m_Running = false;
        m_Conn?.Close();
        m_Conn = null;
        m_Events.Clear();
        m_Peers.Clear();
        Ready = false;
    }

    // ---- 小さな JSON（サーバーからの {"t":"...","code":"...","id":3,"msg":"..."} だけ読めればよい）
    static string JsonField(string json, string key)
    {
        string k = "\"" + key + "\":";
        int i = json.IndexOf(k, StringComparison.Ordinal);
        if (i < 0) return "";
        i += k.Length;
        while (i < json.Length && json[i] == ' ') i++;
        if (i < json.Length && json[i] == '"')
        {
            var sb = new StringBuilder();
            for (i++; i < json.Length && json[i] != '"'; i++)
            {
                if (json[i] == '\\' && i + 1 < json.Length) { i++; sb.Append(json[i] == 'n' ? '\n' : json[i]); }
                else sb.Append(json[i]);
            }
            return sb.ToString();
        }
        int j = i;
        while (j < json.Length && json[j] != ',' && json[j] != '}') j++;
        return json.Substring(i, j - i).Trim();
    }

    static string Escape(string s) => (s ?? "").Replace("\\", "").Replace("\"", "");
}

// ================================================================ WebSocket 本体（Web 版と PC 版で中身が違う）

interface IWsConnection
{
    bool IsOpen { get; }
    bool IsClosed { get; }
    string Error { get; }
    void Send(byte[] data);
    void SendText(string text);
    bool TryReceive(out byte[] data, out bool isText);
    void Close();
}

static class WsConnections
{
    public static IWsConnection Open(string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return new BrowserWs(url);
#else
        return new DotNetWs(url);
#endif
    }

    /// <summary>Web 版で、ゲームを開いているページと同じサーバーの /ws。わからなければ空</summary>
    public static string PageUrl()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return BrowserWs.WSB_PageUrl();
#else
        return "";
#endif
    }
}

#if UNITY_WEBGL && !UNITY_EDITOR
class BrowserWs : IWsConnection
{
    [DllImport("__Internal")] static extern int WSB_Open(string url);
    [DllImport("__Internal")] static extern int WSB_State(int id);
    [DllImport("__Internal")] static extern int WSB_NextLength(int id);
    [DllImport("__Internal")] static extern int WSB_NextIsText(int id);
    [DllImport("__Internal")] static extern void WSB_Recv(int id, byte[] buffer, int length);
    [DllImport("__Internal")] static extern void WSB_Send(int id, byte[] buffer, int length);
    [DllImport("__Internal")] static extern void WSB_SendText(int id, string text);
    [DllImport("__Internal")] static extern void WSB_Close(int id);
    [DllImport("__Internal")] static extern string WSB_Error(int id);
    [DllImport("__Internal")] public static extern string WSB_PageUrl();

    readonly int m_Id;
    bool m_Closed;

    public BrowserWs(string url) => m_Id = WSB_Open(url);

    public bool IsOpen => !m_Closed && WSB_State(m_Id) == 1;
    public bool IsClosed => m_Closed || WSB_State(m_Id) == 2;
    public string Error => m_Closed ? "" : WSB_Error(m_Id);
    public void Send(byte[] data) { if (!m_Closed) WSB_Send(m_Id, data, data.Length); }
    public void SendText(string text) { if (!m_Closed) WSB_SendText(m_Id, text); }

    public bool TryReceive(out byte[] data, out bool isText)
    {
        data = null; isText = false;
        if (m_Closed) return false;
        int len = WSB_NextLength(m_Id);
        if (len < 0) return false;
        isText = WSB_NextIsText(m_Id) != 0;
        data = new byte[len];
        WSB_Recv(m_Id, data, len);
        return true;
    }

    public void Close()
    {
        if (m_Closed) return;
        m_Closed = true;
        WSB_Close(m_Id);
    }
}
#else
class DotNetWs : IWsConnection
{
    readonly ClientWebSocket m_Ws = new ClientWebSocket();
    readonly CancellationTokenSource m_Cancel = new CancellationTokenSource();
    readonly ConcurrentQueue<(byte[] data, bool text)> m_In = new ConcurrentQueue<(byte[], bool)>();
    readonly BlockingCollection<(byte[] data, bool text)> m_Out = new BlockingCollection<(byte[], bool)>();
    volatile bool m_Open, m_Closed;
    volatile string m_Error = "";

    public DotNetWs(string url)
    {
        var uri = new Uri(url);
        Task.Run(() => Run(uri));
    }

    async Task Run(Uri uri)
    {
        try
        {
            await m_Ws.ConnectAsync(uri, m_Cancel.Token);
            m_Open = true;
            _ = Task.Run(SendLoop);
            var buf = new byte[64 * 1024];
            var msg = new System.IO.MemoryStream();
            while (!m_Cancel.IsCancellationRequested)
            {
                var r = await m_Ws.ReceiveAsync(new ArraySegment<byte>(buf), m_Cancel.Token);
                if (r.MessageType == WebSocketMessageType.Close) break;
                msg.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                m_In.Enqueue((msg.ToArray(), r.MessageType == WebSocketMessageType.Text));
                msg.SetLength(0);
            }
        }
        catch (Exception e)
        {
            if (!m_Cancel.IsCancellationRequested) m_Error = e.Message;
        }
        m_Open = false;
        m_Closed = true;
        m_Out.CompleteAdding();
    }

    async Task SendLoop()
    {
        try
        {
            foreach (var (data, text) in m_Out.GetConsumingEnumerable(m_Cancel.Token))
                await m_Ws.SendAsync(new ArraySegment<byte>(data), text ? WebSocketMessageType.Text : WebSocketMessageType.Binary, true, m_Cancel.Token);
        }
        catch (Exception) { }
    }

    public bool IsOpen => m_Open && !m_Closed;
    public bool IsClosed => m_Closed;
    public string Error => m_Error;
    public void Send(byte[] data) => Enqueue(data, false);
    public void SendText(string text) => Enqueue(Encoding.UTF8.GetBytes(text), true);

    void Enqueue(byte[] data, bool text)
    {
        if (!IsOpen) return;
        try { m_Out.TryAdd((data, text)); } catch (InvalidOperationException) { }   // 閉じた直後
    }
    public bool TryReceive(out byte[] data, out bool isText)
    {
        if (m_In.TryDequeue(out var m)) { data = m.data; isText = m.text; return true; }
        data = null; isText = false;
        return false;
    }

    public void Close()
    {
        if (m_Closed && m_Cancel.IsCancellationRequested) return;
        m_Closed = true;
        try { m_Ws.Abort(); } catch (Exception) { }
        m_Cancel.Cancel();
    }
}
#endif
#endif
