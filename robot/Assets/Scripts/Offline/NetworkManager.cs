// オフライン用の代役（OfflineNetcode.cs の説明を参照）。Unity ではコンポーネント名とファイル名をそろえる必要があるので別ファイル。
#if !HAS_NETCODE
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Netcode
{
    public class NetworkManager : MonoBehaviour
    {
        public const ulong ServerClientId = 0;
        public static NetworkManager Singleton { get; private set; }

        public NetworkConfig NetworkConfig = new NetworkConfig();

        bool m_Running;
        readonly Dictionary<ulong, NetworkClient> m_Clients = new Dictionary<ulong, NetworkClient>();
        readonly List<NetworkClient> m_ClientList = new List<NetworkClient>();
        internal readonly List<NetworkObject> Spawned = new List<NetworkObject>();

        public bool IsServer => m_Running;
        public bool IsClient => m_Running;
        public bool IsHost => m_Running;
        public bool IsListening => m_Running;
        public ulong LocalClientId => 0;
        public IReadOnlyDictionary<ulong, NetworkClient> ConnectedClients => m_Clients;
        public IReadOnlyList<NetworkClient> ConnectedClientsList => m_ClientList;
        public string DisconnectReason => "";

        public event Action OnServerStarted;
        public event Action<ulong> OnClientConnectedCallback;
        public event Action<ulong> OnClientDisconnectCallback;

        void Awake() => Singleton = this;

        void OnDestroy()
        {
            if (Singleton == this) Singleton = null;
        }

        public void AddNetworkPrefab(GameObject prefab) => NetworkConfig.Prefabs.Add(prefab);

        public bool StartHost()
        {
            if (m_Running) return false;
            Singleton = this;
            m_Running = true;
            var me = new NetworkClient { ClientId = ServerClientId };
            m_Clients[me.ClientId] = me;
            m_ClientList.Add(me);
            OnServerStarted?.Invoke();
            OnClientConnectedCallback?.Invoke(me.ClientId);
            return true;
        }

        /// <summary>オフラインでは参加先がないので失敗する。</summary>
        public bool StartClient() => false;

        public void Shutdown(bool discardMessageQueue = false)
        {
            if (!m_Running) return;
            foreach (var o in Spawned.ToArray()) o.Despawn(true);
            Spawned.Clear();
            OnClientDisconnectCallback?.Invoke(ServerClientId);
            m_Clients.Clear();
            m_ClientList.Clear();
            m_Running = false;
        }

        public void DisconnectClient(ulong clientId, string reason = null) { }

        internal void SetPlayerObject(ulong clientId, NetworkObject obj)
        {
            if (m_Clients.TryGetValue(clientId, out var c)) c.PlayerObject = obj;
        }
    }
}
#endif
