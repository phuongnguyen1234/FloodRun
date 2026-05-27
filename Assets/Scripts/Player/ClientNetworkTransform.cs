using Unity.Netcode.Components;
using UnityEngine;

namespace Multiplayer
{
    /// <summary>
    /// Mở rộng NetworkTransform để cho phép Client (Owner) có quyền điều khiển vị trí.
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}