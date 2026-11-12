#if HAS_NETCODE
using System;
using Unity.Netcode;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Web 版のマルチ：ブラウザどうしを WebRTC で直接つなぐ Netcode の通信方式（PeerJS を使う。Plugins/WebGL/PeerBridge.jslib）。
///
/// - 部屋を作る人：PeerJS の無料の仲介サーバーに4けたの部屋番号で登録し、入ってきた人とそれぞれ直接つながる
/// - 入る人：部屋番号でホストを探して、直接つながる
/// - サーバーを用意しなくてよいので、GitHub Pages に置いた Web 版だけでマルチができる
///
/// ブラウザの機能を使うので、Web 版でしか動かない（エディタや PC 版では StartServer / StartClient が失敗する）。
/// </summary>
public class PeerTransport : NetworkTransport
{
    /// <summary>入る部屋の番号（入る人だけ）</summary>
    public string JoinCode = "";

    /// <summary>ホストがもらった部屋番号（もらうまでは空）</summary>
    public string RoomCode { get; private set; } = "";
    /// <summary>部屋を作れた（ホスト）／ホストにつながった（入る人）</summary>
    public bool Ready { get; private set; }
    /// <summary>うまくいかなかったときの理由</summary>
    public string LastError { get; private set; } = "";

    public override ulong ServerClientId => 0;

    /// <summary>この環境で使えるか（Web 版だけ）</summary>
    public static bool Supported =>
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
        false;
#endif

    bool m_IsServer, m_Running;

    public override void Initialize(NetworkManager networkManager = null) { }

    public override bool StartServer()
    {
        Reset(true);
        if (!Supported) { LastError = "ブラウザどうしのマルチは Web 版でだけ使えます。"; return false; }
        Native.Host();
        return true;
    }

    public override bool StartClient()
    {
        Reset(false);
        if (!Supported) { LastError = "ブラウザどうしのマルチは Web 版でだけ使えます。"; return false; }
        Native.Join(JoinCode ?? "");
        return true;
    }

    void Reset(bool server)
    {
        m_IsServer = server;
        m_Running = true;
        RoomCode = LastError = "";
        Ready = false;
    }

    public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
    {
        if (!m_Running) return;
        var buf = new byte[payload.Count];
        Buffer.BlockCopy(payload.Array, payload.Offset, buf, 0, payload.Count);
        Native.Send(m_IsServer ? (int)clientId : 0, buf);
    }

    public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
    {
        receiveTime = Time.realtimeSinceStartup;
        clientId = 0;
        payload = default;
        if (!m_Running) return NetworkEvent.Nothing;

        // 部屋番号が決まった・つながった・失敗した、を読み取る
        int state = Native.State();
        if (state == 1 && !Ready) { Ready = true; if (m_IsServer) RoomCode = Native.Code(); }
        if (state == 2 && string.IsNullOrEmpty(LastError)) LastError = Native.Error();

        int type = Native.PeekType();
        if (type < 0) return NetworkEvent.Nothing;
        int id = Native.PeekId();
        int len = Native.PeekLength();
        var data = new byte[len];
        Native.Pop(data, len);
        clientId = m_IsServer ? (ulong)id : ServerClientId;
        switch (type)
        {
            case 0:
                payload = new ArraySegment<byte>(data);
                return NetworkEvent.Data;
            case 1:
                return NetworkEvent.Connect;
            case 2:
                return NetworkEvent.Disconnect;
            default:
                if (string.IsNullOrEmpty(LastError)) LastError = Native.Error();
                // ホストは部屋ごと失敗、入る人はホストから切れた
                return m_IsServer ? NetworkEvent.TransportFailure : NetworkEvent.Disconnect;
        }
    }

    public override void DisconnectRemoteClient(ulong clientId) => Native.Kick((int)clientId);

    public override void DisconnectLocalClient() => Shutdown();

    public override ulong GetCurrentRtt(ulong clientId) => 0;

    public override void Shutdown()
    {
        if (m_Running) Native.Close();
        m_Running = false;
        Ready = false;
    }

    // ---------------------------------------------------------------- ブラウザ側（PeerBridge.jslib）

    static class Native
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void PJB_Host();
        [DllImport("__Internal")] static extern void PJB_Join(string code);
        [DllImport("__Internal")] static extern int PJB_State();
        [DllImport("__Internal")] static extern string PJB_Code();
        [DllImport("__Internal")] static extern string PJB_Error();
        [DllImport("__Internal")] static extern int PJB_PeekType();
        [DllImport("__Internal")] static extern int PJB_PeekId();
        [DllImport("__Internal")] static extern int PJB_PeekLength();
        [DllImport("__Internal")] static extern void PJB_Pop(byte[] buffer, int length);
        [DllImport("__Internal")] static extern void PJB_Send(int to, byte[] buffer, int length);
        [DllImport("__Internal")] static extern void PJB_Kick(int id);
        [DllImport("__Internal")] static extern void PJB_Close();

        public static void Host() => PJB_Host();
        public static void Join(string code) => PJB_Join(code);
        public static int State() => PJB_State();
        public static string Code() => PJB_Code();
        public static string Error() => PJB_Error();
        public static int PeekType() => PJB_PeekType();
        public static int PeekId() => PJB_PeekId();
        public static int PeekLength() => PJB_PeekLength();
        public static void Pop(byte[] buffer, int length) => PJB_Pop(buffer, length);
        public static void Send(int to, byte[] data) => PJB_Send(to, data, data.Length);
        public static void Kick(int id) => PJB_Kick(id);
        public static void Close() => PJB_Close();
#else
        public static void Host() { }
        public static void Join(string code) { }
        public static int State() => 2;
        public static string Code() => "";
        public static string Error() => "ブラウザどうしのマルチは Web 版でだけ使えます。";
        public static int PeekType() => -1;
        public static int PeekId() => 0;
        public static int PeekLength() => 0;
        public static void Pop(byte[] buffer, int length) { }
        public static void Send(int to, byte[] data) { }
        public static void Kick(int id) { }
        public static void Close() { }
#endif
    }
}
#endif
