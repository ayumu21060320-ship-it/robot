using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
#if HAS_NETCODE && HAS_RELAY
using System.Collections.Generic;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
#endif

/// <summary>
/// 「部屋番号」でつながるオンラインの部屋。Unity の Relay（中継サーバー）を使う。
///
/// - 部屋を作る人（ホスト）が Relay に場所を借りると、6文字の部屋番号（例：K7M2QX）がもらえる
/// - ほかの人はその部屋番号を入れると、Relay を通してホストにつながる
/// - Web（ブラウザ）でも動くように、通信は WebSocket（wss）を使う
///
/// 使うには Netcode for GameObjects と Multiplayer Services（Relay）のパッケージ、
/// Unity Cloud のプロジェクトとのリンクが必要（README 参照）。どちらかが無いときは Available が false になる。
/// </summary>
public static class OnlineRooms
{
#if HAS_NETCODE && HAS_RELAY
    public static bool Available => true;
#else
    public static bool Available => false;
#endif

    /// <summary>今いる部屋の部屋番号（部屋に入っていなければ空）</summary>
    public static string RoomCode { get; private set; } = "";

    public static void Clear() => RoomCode = "";

    /// <summary>部屋番号の入力をそろえる（大文字・英数字だけ）</summary>
    public static string Normalize(string code)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char ch in code.Trim().ToUpperInvariant())
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
        return sb.ToString();
    }

#if HAS_NETCODE && HAS_RELAY
    const string Protocol = "wss";   // ブラウザでも使えるよう、全員 WebSocket でつなぐ

    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    static RelayServerEndpoint PickEndpoint(List<RelayServerEndpoint> endpoints)
    {
        foreach (var e in endpoints) if (e.ConnectionType == Protocol) return e;
        throw new Exception("Relay に WebSocket の接続先がありませんでした。");
    }

    static UnityTransport Transport(NetworkManager nm)
    {
        var t = nm.NetworkConfig.NetworkTransport as UnityTransport;
        if (t == null) throw new Exception("NetworkManager に UnityTransport がありません（メニュー「Robot → ゲームのシーンを作成」を実行し直してください）。");
        t.UseWebSockets = true;
        return t;
    }

    /// <summary>部屋を作ってホストとして始める。成功したら部屋番号を返す。</summary>
    public static async Task<string> CreateRoom(NetworkManager nm, int maxPlayers)
    {
        await SignIn();
        Allocation a = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        string code = await RelayService.Instance.GetJoinCodeAsync(a.AllocationId);
        var ep = PickEndpoint(a.ServerEndpoints);
        var data = new RelayServerData(ep.Host, (ushort)ep.Port, a.AllocationIdBytes, a.ConnectionData, a.ConnectionData, a.Key, ep.Secure, true);
        Transport(nm).SetRelayServerData(data);
        if (!nm.StartHost()) throw new Exception("ホストを始められませんでした。");
        RoomCode = code;
        return code;
    }

    /// <summary>部屋番号で部屋に入る。</summary>
    public static async Task JoinRoom(NetworkManager nm, string code)
    {
        code = Normalize(code);
        await SignIn();
        JoinAllocation j = await RelayService.Instance.JoinAllocationAsync(code);
        var ep = PickEndpoint(j.ServerEndpoints);
        var data = new RelayServerData(ep.Host, (ushort)ep.Port, j.AllocationIdBytes, j.ConnectionData, j.HostConnectionData, j.Key, ep.Secure, true);
        Transport(nm).SetRelayServerData(data);
        if (!nm.StartClient()) throw new Exception("接続を始められませんでした。");
        RoomCode = code;
    }
#else
    public static Task<string> CreateRoom(NetworkManager nm, int maxPlayers) =>
        Task.FromException<string>(new Exception("オンラインの部屋には Netcode と Multiplayer Services のパッケージが必要です（README 参照）。"));

    public static Task JoinRoom(NetworkManager nm, string code) =>
        Task.FromException(new Exception("オンラインの部屋には Netcode と Multiplayer Services のパッケージが必要です（README 参照）。"));
#endif

    /// <summary>エラーを、遊ぶ人にわかる短い日本語にする</summary>
    public static string Explain(Exception e, bool joining)
    {
        string m = e.Message ?? "";
        if (joining && (m.Contains("join code") || m.Contains("Join code") || m.Contains("not found") || m.Contains("NotFound") || m.Contains("400") || m.Contains("404")))
            return "その部屋番号の部屋が見つかりません。番号を確かめてください（部屋を作った人が抜けると部屋は消えます）。";
        if (m.Contains("project") || m.Contains("Project") || m.Contains("cloud") || m.Contains("Cloud"))
            return "Unity Cloud のプロジェクトにリンクされていません（README の「オンラインの準備」を参照）。\n" + m;
        if (m.Contains("network") || m.Contains("Network") || m.Contains("timeout") || m.Contains("Timeout"))
            return "インターネットにつながりませんでした。回線を確かめてください。\n" + m;
        return m;
    }
}
