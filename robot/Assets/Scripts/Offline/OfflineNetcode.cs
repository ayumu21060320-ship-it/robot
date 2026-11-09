// Netcode for GameObjects が入っていないとき（学校のネットワークなどでパッケージを取得できないとき）だけ使う、
// オフライン用の代役。ゲームのコードは本物の Netcode と同じ書き方のまま、1台のPCで動く。
// Netcode が入ると HAS_NETCODE が定義され、このファイルは空になる（Game.asmdef の versionDefines を参照）。
#if !HAS_NETCODE
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Netcode
{
    public enum NetworkVariableReadPermission { Everyone, Owner }
    public enum NetworkVariableWritePermission { Server, Owner }

    public class NetworkVariable<T>
    {
        T m_Value;

        public NetworkVariable(T value = default,
            NetworkVariableReadPermission readPerm = NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission writePerm = NetworkVariableWritePermission.Server)
        {
            m_Value = value;
        }

        public delegate void OnValueChangedDelegate(T previousValue, T newValue);
        public OnValueChangedDelegate OnValueChanged;

        public T Value
        {
            get => m_Value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(m_Value, value)) return;
                var prev = m_Value;
                m_Value = value;
                OnValueChanged?.Invoke(prev, value);
            }
        }
    }

    public class NetworkList<T> where T : unmanaged, IEquatable<T>
    {
        readonly List<T> m_List = new List<T>();

        public NetworkList(IEnumerable<T> values = null,
            NetworkVariableReadPermission readPerm = NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission writePerm = NetworkVariableWritePermission.Server)
        {
            if (values != null) m_List.AddRange(values);
        }

        public int Count => m_List.Count;
        public void Add(T item) => m_List.Add(item);
        public void Clear() => m_List.Clear();
        public T this[int index] { get => m_List[index]; set => m_List[index] = value; }
    }

    public enum SendTo { Owner, NotOwner, Server, NotServer, Me, NotMe, Everyone, ClientsAndHost }

    /// <summary>オフラインでは送り先がすべて自分なので、RPC はふつうのメソッド呼び出しとしてそのまま動く。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class RpcAttribute : Attribute
    {
        public RpcAttribute(SendTo target) { }
    }

    public struct RpcReceiveParams { public ulong SenderClientId; }
    public struct RpcParams { public RpcReceiveParams Receive; }

    public class NetworkClient
    {
        public ulong ClientId;
        public NetworkObject PlayerObject;
    }

    public class NetworkPrefabs
    {
        readonly List<GameObject> m_Prefabs = new List<GameObject>();
        public bool Contains(GameObject prefab) => m_Prefabs.Contains(prefab);
        internal void Add(GameObject prefab) { if (!m_Prefabs.Contains(prefab)) m_Prefabs.Add(prefab); }
    }

    public class NetworkTransport : MonoBehaviour { }

    [Serializable]
    public class NetworkConfig
    {
        public NetworkTransport NetworkTransport;
        public bool EnableSceneManagement;
        public bool ConnectionApproval;
        public GameObject PlayerPrefab;
        NetworkPrefabs m_Prefabs; // 保存しない（実行中だけ使う）
        public NetworkPrefabs Prefabs => m_Prefabs ??= new NetworkPrefabs();
    }

    public abstract class NetworkBehaviour : MonoBehaviour
    {
        NetworkObject m_Object;

        public NetworkManager NetworkManager => NetworkManager.Singleton;
        public NetworkObject NetworkObject => m_Object;
        public bool IsSpawned => m_Object != null && m_Object.IsSpawned;
        public bool IsServer => IsSpawned;
        public bool IsClient => IsSpawned;
        public bool IsHost => IsSpawned;
        public bool IsOwner => IsSpawned && m_Object.OwnerClientId == NetworkManager.ServerClientId;
        public ulong OwnerClientId => m_Object != null ? m_Object.OwnerClientId : 0;

        public virtual void OnNetworkSpawn() { }
        public virtual void OnNetworkDespawn() { }

        internal void InternalSpawn(NetworkObject obj)
        {
            m_Object = obj;
            OnNetworkSpawn();
        }

        internal void InternalDespawn() => OnNetworkDespawn();
    }
}

#endif
