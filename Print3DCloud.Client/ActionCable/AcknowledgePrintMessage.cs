using System;
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
        /// <param name="exception">The error associated with this acknowledgement, if any.</param>
        public AcknowledgePrintMessage(Exception? exception = null)
            : base("acknowledge_print")
        {
            this.Success = exception == null;
            this.ErrorType = exception?.GetType().Name;
            this.ErrorMessage = exception?.Message;
        }

        /// <summary>
        /// Gets a value indicating whether the operation has succeeded or not.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Gets the error associated with this acknowledgement, if any.
        /// </summary>
        public string? ErrorType { get; }

        /// <summary>
        /// Gets the error associated with this acknowledgement, if any.
        /// </summary>
        public string? ErrorMessage { get; }
    }
}