using Print3DCloud.Client.Printers;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message containing UltiGCode settings per extruder.
    /// </summary>
    /// <param name="MessageId">The ID of the message to send back when acknowledging.</param>
    /// <param name="UltiGCodeSettings">Array of UltiGCode settings.</param>
    public record UltiGCodeSettingsMessage(string MessageId, UltiGCodeSettings[] UltiGCodeSettings) : AcknowledgeableMessage(MessageId);
}