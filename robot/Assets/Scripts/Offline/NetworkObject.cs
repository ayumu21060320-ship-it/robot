// オフライン用の代役（OfflineNetcode.cs の説明を参照）。Unity ではコンポーネント名とファイル名をそろえる必要があるので別ファイル。
#if !HAS_NETCODE
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Netcode
{
    public class NetworkObject : MonoBehaviour
    {
        NetworkManager m_Manager;
        public bool IsSpawned { get; private set; }
        public ulong OwnerClientId { get; private set; }

        public void Spawn(bool destroyWithScene = false) => DoSpawn(NetworkManager.ServerClientId);

        public void SpawnAsPlayerObject(ulong clientId, bool destroyWithScene = false)
        {
            DoSpawn(clientId);
            m_Manager.SetPlayerObject(clientId, this);
        }

        void DoSpawn(ulong owner)
        {
            m_Manager = NetworkManager.Singleton;
            OwnerClientId = owner;
            IsSpawned = true;
            m_Manager.Spawned.Add(this);
            foreach (var b in GetComponents<NetworkBehaviour>()) b.InternalSpawn(this);
        }

        public void Despawn(bool destroy = true)
        {
            if (!IsSpawned) return;
            IsSpawned = false;
            foreach (var b in GetComponents<NetworkBehaviour>()) b.InternalDespawn();
            if (m_Manager) m_Manager.Spawned.Remove(this);
            if (destroy) Destroy(gameObject);
        }
    }
}
#endif
