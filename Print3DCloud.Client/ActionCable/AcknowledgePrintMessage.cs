using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message sent after a print request has been processed.
    /// </summary>
    public class AcknowledgePrintMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AcknowledgePrintMessage"/> class.
        /// </summary>
        public AcknowledgePrintMessage()
            : base("acknowledge_print")
        {
        }
    }
}