namespace Core
{
    /// <summary>
    /// Shared storage for room creation data passed from Home to the Multiplayer scene.
    /// </summary>
    public static class MultiplayerRoomInfoCache
    {
        public static string PendingRoomId;
        public static string PendingPasscode;
        public static int PendingMaxPlayers;
        public static string PendingHostName; 
    }
}
