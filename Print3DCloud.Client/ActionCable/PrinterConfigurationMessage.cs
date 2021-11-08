using Print3DCloud.Client.Models;

namespace Print3DCloud.Client.ActionCable
{
    internal record PrinterConfigurationMessage(string MessageId, Printer Printer) : AcknowledgeableMessage(MessageId);
}
