using System.Runtime.Serialization;

namespace ActionCableSharp.Internal
{
    internal enum MessageType
    {
        None,
        Welcome,
        Disconnect,
        Ping,
        [EnumMember(Value = "confirm_subscription")] Confirmation,
        [EnumMember(Value = "reject_subscription")] Rejection
    }
}
