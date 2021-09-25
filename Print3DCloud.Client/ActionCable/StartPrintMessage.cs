using ActionCableSharp;

namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Message indicating a print should be started.
    /// </summary>
    internal class StartPrintMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StartPrintMessage"/> class.
        /// </summary>
        public StartPrintMessage()
            : base("start_print")
        {
        }

        /// <summary>
        /// Gets or sets the ID of the print to start.
        /// </summary>
        public long PrintId { get; set; }

        /// <summary>
        /// Gets or sets the URL of the file to print.
        /// </summary>
        public string DownloadUrl { get; set; } = null!;
    }
}