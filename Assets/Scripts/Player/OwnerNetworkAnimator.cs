using Unity.Netcode.Components;

namespace Core.Multiplayer
{
    /// <summary>
    /// Phiên bản NetworkAnimator cho phép Owner (Client) có quyền điều khiển animation.
    /// </summary>
    public class OwnerNetworkAnimator : NetworkAnimator
    {
        protected override bool OnIsServerAuthoritative()
        {
            return false;
        }
    }
}