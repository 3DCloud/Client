using System;
using System.Diagnostics.CodeAnalysis;
using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message sent after a print request has been processed.
    /// </summary>
    public class AcknowledgeMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AcknowledgeMessage"/> class.
        /// </summary>
        /// <param name="messageId">ID of the message to acknowledge.</param>
        /// <param name="error">Exception that occurred, if any.</param>
        public AcknowledgeMessage(string messageId, Exception? error = null)
            : base("acknowledge")
        {
            this.MessageId = messageId;

            if (error != null)
            {
                this.Success = false;
                this.ErrorMessage = error.Message;
                this.StackTrace = error.StackTrace;
            }
            else
            {
                this.Success = true;
            }
        }

        /// <summary>
        /// Gets or sets the ID of the message to acknowledge.
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the operation was a success or not.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the error message associated with this acknowledgement, if applicable.
        /// </summary>
        [MemberNotNullWhen(false, nameof(Success))]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the error stack trace associated with this acknowledgement, if applicable.
        /// </summary>
        public string? StackTrace { get; set; }
    }
}