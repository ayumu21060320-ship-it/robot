using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
#if HAS_NETCODE && HAS_RELAY
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
#endif

/// <summary>
/// 「部屋番号」でつながるオンラインの部屋。つなぎ方は2通りあり、入っているパッケージで自動で決まる。
///
/// 1. Unity Relay（Multiplayer Services 2.4.1 が入っているとき。将棋のゲームと同じ方法）
///    - 部屋を作ると Relay から6文字の部屋番号（例：K7M2QX）がもらえる。サーバーを用意しなくてよいので、
///      Web 版は GitHub Pages に置くだけで遊べる
///    - 通信は WebSocket（wss）なので、ブラウザでも PC でもつながる
/// 2. 自分のサーバー（Multiplayer Services が無いとき。server/server.js）
///    - 4けたの部屋番号。Web 版は開いたページと同じサーバーへ、エディタ・PC 版はメニューで入れた URL へつなぐ
///
/// 1人用はどちらでもサーバーにつながない。Netcode for GameObjects が入っていないときは1人用だけ（Available = false）。
/// </summary>
public static class OnlineRooms
{
#if HAS_NETCODE
    public static bool Available => true;
#else
    public static bool Available => false;
#endif

#if HAS_NETCODE && HAS_RELAY
    /// <summary>Unity Relay を使う（サーバーの URL はいらない）</summary>
    public static bool UsesRelay => true;
#else
    public static bool UsesRelay => false;
#endif

    const string UrlKey = "titan_server_url";
    public const string LocalUrl = "ws://localhost:8080/ws";

    /// <summary>今いる部屋の部屋番号（部屋に入っていなければ空）</summary>
    public static string RoomCode { get; private set; } = "";

    public static void Clear() => RoomCode = "";

    /// <summary>サーバーの URL を入れる欄がいらないとき true（Relay を使う、または Web 版で開いたページのサーバーにつなぐ）</summary>
    public static bool UrlFromPage => UsesRelay || !string.IsNullOrEmpty(PageUrl());

    /// <summary>つなぐサーバーの URL。Web 版はページと同じサーバー、それ以外は前回入れた URL</summary>
    public static string ServerUrl
    {
        get
        {
            string page = PageUrl();
            if (!string.IsNullOrEmpty(page)) return page;
            try { return PlayerPrefs.GetString(UrlKey, LocalUrl); } catch (Exception) { return LocalUrl; }
        }
        set
        {
            try { PlayerPrefs.SetString(UrlKey, value); PlayerPrefs.Save(); } catch (Exception) { }
        }
    }

    /// <summary>
    /// 入力された URL を WebSocket の URL にそろえる。
    /// 例：「example.com」→「wss://example.com/ws」、「http://localhost:8080」→「ws://localhost:8080/ws」
    /// </summary>
    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0) return LocalUrl;
        if (url.StartsWith("https://")) url = "wss://" + url.Substring(8);
        else if (url.StartsWith("http://")) url = "ws://" + url.Substring(7);
        else if (!url.StartsWith("ws://") && !url.StartsWith("wss://"))
            url = (url.StartsWith("localhost") || url.StartsWith("127.") || url.StartsWith("192.168.") ? "ws://" : "wss://") + url;
        int scheme = url.IndexOf("://", StringComparison.Ordinal) + 3;
        int slash = url.IndexOf('/', scheme);
        if (slash < 0) url += "/ws";
        else if (slash == url.Length - 1) url += "ws";
        return url;
    }

    /// <summary>部屋番号の入力をそろえる（数字と英字だけ）</summary>
    public static string Normalize(string code)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in (code ?? "").Trim().ToUpperInvariant())
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
        return sb.ToString();
    }

#if HAS_NETCODE
    static string PageUrl() => WsConnections.PageUrl();

    static WebSocketTransport UseTransport(NetworkManager nm)
    {
        var t = nm.GetComponent<WebSocketTransport>();
        if (t == null) t = nm.gameObject.AddComponent<WebSocketTransport>();
        nm.NetworkConfig.NetworkTransport = t;
        return t;
    }

    /// <summary>うまくいかなかったときの理由（切断されたときにも使う）</summary>
    public static string LastError(NetworkManager nm)
    {
        var t = nm != null ? nm.GetComponent<WebSocketTransport>() : null;
        return t != null ? t.LastError : "";
    }

    /// <summary>1人で遊ぶ（サーバーにはつながない）</summary>
    public static bool StartSolo(NetworkManager nm)
    {
        var t = UseTransport(nm);
        t.Offline = true;
        RoomCode = "";
        return nm.StartHost();
    }

#if HAS_RELAY
    // ---------------- Unity Relay（将棋のゲームの ShogiOnline.cs と同じつなぎ方）

    /// <summary>Unity のサービスに名前なし（匿名）でログインする。初回だけ時間がかかる</summary>
    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    static UnityTransport UseRelayTransport(NetworkManager nm)
    {
        var t = nm.GetComponent<UnityTransport>();
        if (t == null) t = nm.gameObject.AddComponent<UnityTransport>();
        t.UseWebSockets = true;            // ブラウザでもつながるように WebSocket
        nm.NetworkConfig.NetworkTransport = t;
        return t;
    }

    /// <summary>Relay に部屋を作ってホストとして始める。成功したら部屋番号（参加コード）を返す。</summary>
    public static async Task<string> CreateRoom(NetworkManager nm)
    {
        try
        {
            await SignIn();
            var allocation = await RelayService.Instance.CreateAllocationAsync(GameBootstrap.MaxPlayers - 1);
            string code = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            UseRelayTransport(nm).SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "wss"));
            if (!nm.StartHost()) throw new Exception("ホストを始められませんでした。");
            RoomCode = code;
            return code;
        }
        catch (Exception e) { throw new Exception(Explain(e, false), e); }
    }

    /// <summary>部屋番号（参加コード）で部屋に入る。</summary>
    public static async Task JoinRoom(NetworkManager nm, string code)
    {
        code = Normalize(code);
        try
        {
            await SignIn();
            var allocation = await RelayService.Instance.JoinAllocationAsync(code);
            UseRelayTransport(nm).SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "wss"));
            if (!nm.StartClient()) throw new Exception("接続を始められませんでした。");
        }
        catch (Exception e) { throw new Exception(Explain(e, true), e); }
        await WaitFor(() => nm.IsConnectedClient || !nm.IsListening);
        if (!nm.IsConnectedClient) throw new Exception("ホストにつながりませんでした。部屋番号を確かめて、もう一度試してください。");
        RoomCode = code;
    }

    /// <summary>Relay のエラーを、遊ぶ人にわかる短い日本語にする</summary>
    static string Explain(Exception e, bool joining)
    {
        string m = e.Message ?? "";
        if (joining && (m.Contains("join code") || m.Contains("Join code") || m.Contains("not found") || m.Contains("NotFound") || m.Contains("400") || m.Contains("404")))
            return "その部屋番号の部屋が見つかりません。番号を確かめてください（部屋を作った人が抜けると部屋は消えます）。";
        if (m.Contains("project") || m.Contains("Project") || m.Contains("cloud") || m.Contains("Cloud"))
            return "Unity Cloud のプロジェクトにリンクされていません（README の「オンラインの準備」）。\n" + m;
        if (m.Contains("network") || m.Contains("Network") || m.Contains("timeout") || m.Contains("Timeout") || m.Contains("connect"))
            return "Unity のサーバーにつながりませんでした。回線を確かめてください（学校のネットワークでは遮断されることがあります）。\n" + m;
        return m;
    }
#else
    // ---------------- 自分のサーバー（server/server.js）

    /// <summary>サーバーに部屋を作ってもらい、ホストとして始める。成功したら部屋番号を返す。</summary>
    public static async Task<string> CreateRoom(NetworkManager nm)
    {
        var t = UseTransport(nm);
        t.Offline = false;
        t.ServerUrl = ServerUrl;
        if (!nm.StartHost()) throw new Exception(Or(t.LastError, "ホストを始められませんでした。"));
        await WaitFor(() => t.Ready || !string.IsNullOrEmpty(t.LastError) || !nm.IsListening);
        if (!t.Ready || string.IsNullOrEmpty(t.RoomCode)) throw new Exception(Or(t.LastError, "サーバーから返事がありませんでした。"));
        RoomCode = t.RoomCode;
        return RoomCode;
    }

    /// <summary>部屋番号で部屋に入る。</summary>
    public static async Task JoinRoom(NetworkManager nm, string code)
    {
        var t = UseTransport(nm);
        t.Offline = false;
        t.ServerUrl = ServerUrl;
        t.JoinCode = Normalize(code);
        if (!nm.StartClient()) throw new Exception(Or(t.LastError, "接続を始められませんでした。"));
        await WaitFor(() => t.Ready || !string.IsNullOrEmpty(t.LastError) || !nm.IsListening);
        if (!t.Ready) throw new Exception(Or(t.LastError, "サーバーから返事がありませんでした。"));
        RoomCode = t.JoinCode;
    }
#endif

    /// <summary>条件がそろうまで（最大12秒）1フレームずつ待つ。Web 版はスレッドが使えないので Task.Delay は使わない</summary>
    static async Task WaitFor(Func<bool> done)
    {
        float until = Time.realtimeSinceStartup + 12f;
        while (!done() && Time.realtimeSinceStartup < until) await Task.Yield();
    }

    static string Or(string a, string b) => string.IsNullOrEmpty(a) ? b : a;
#else
    static string PageUrl() => "";

    public static string LastError(NetworkManager nm) => "";

    public static bool StartSolo(NetworkManager nm) => nm.StartHost();

    public static Task<string> CreateRoom(NetworkManager nm) =>
        Task.FromException<string>(new Exception("オンラインには Netcode for GameObjects のパッケージが必要です。"));

    public static Task JoinRoom(NetworkManager nm, string code) =>
        Task.FromException(new Exception("オンラインには Netcode for GameObjects のパッケージが必要です。"));
#endif
}
