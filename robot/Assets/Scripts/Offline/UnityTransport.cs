// オフライン用の代役（OfflineNetcode.cs の説明を参照）。
#if !HAS_NETCODE
namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : Unity.Netcode.NetworkTransport
    {
        public void SetConnectionData(string ipv4Address, ushort port, string listenAddress = null) { }
    }
}
#endif
