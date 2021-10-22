namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message indicating a print should be started.
    /// </summary>
    /// <param name="MessageId">The ID of the message to send back when acknowledging.</param>
    /// <param name="PrintId">The ID of the print being started.</param>
    /// <param name="DownloadUrl">The URL from which the file to be printed can be downloaded.</param>
    internal record StartPrintMessage(string MessageId, long PrintId, string DownloadUrl) : AcknowledgeableMessage(MessageId);
}