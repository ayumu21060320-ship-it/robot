using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 「部屋番号」でつながるオンラインの部屋。自分で用意したサーバー（server/server.js）を通して通信する。
///
/// - 部屋を作る人（ホスト）がサーバーに部屋を作ってもらうと、4けたの部屋番号（例：4821）がもらえる
/// - ほかの人はその部屋番号を入れると、サーバーを通してホストにつながる
/// - Web 版は、ゲームを開いたページと同じサーバーに自動でつなぐ。エディタと PC 版はメニューで URL を入れる
///
/// 通信の中身は WebSocketTransport.cs。Netcode for GameObjects が入っていないときは1人用だけ（Available = false）。
/// </summary>
public static class OnlineRooms
{
#if HAS_NETCODE
    public static bool Available => true;
#else
    public static bool Available => false;
#endif

    const string UrlKey = "titan_server_url";
    public const string LocalUrl = "ws://localhost:8080/ws";

    /// <summary>今いる部屋の部屋番号（部屋に入っていなければ空）</summary>
    public static string RoomCode { get; private set; } = "";

    public static void Clear() => RoomCode = "";

    /// <summary>Web 版で、ゲームを開いたページのサーバーにつなぐとき true（URL を入れる欄はいらない）</summary>
    public static bool UrlFromPage => !string.IsNullOrEmpty(PageUrl());

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
