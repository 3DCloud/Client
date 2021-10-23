using System;
using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message to send when a print-related event occurs.
    /// </summary>
    public class PrintEventMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PrintEventMessage"/> class.
        /// </summary>
        /// <param name="evt">Event type.</param>
        /// <param name="exception">Exception related to the event, if any.</param>
        public PrintEventMessage(PrintEventType evt, Exception? exception = null)
            : base("print_event")
        {
            this.EventType = evt;

            this.ErrorMessage = exception?.Message;
            this.StackTrace = exception?.StackTrace;
        }

        /// <summary>
        /// Gets or sets the type of event.
        /// </summary>
        public PrintEventType EventType { get; set; }

        /// <summary>
        /// Gets or sets the error message associated with this event, if applicable.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the error stack trace associated with this event, if applicable.
        /// </summary>
        public string? StackTrace { get; set; }
    }
}