namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message that can be acknowledged by using the supplied ID.
    /// </summary>
    /// <param name="MessageId">The ID of the message to send back when acknowledging.</param>
    public record AcknowledgeableMessage(string MessageId);
}