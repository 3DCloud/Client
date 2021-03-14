namespace ActionCableSharp.Tests
{
    /// <summary>
    /// Sample Action Cable message.
    /// </summary>
    internal class SampleMessage : ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SampleMessage"/> class.
        /// </summary>
        /// <param name="content">Message's content.</param>
        public SampleMessage(string content)
            : base("sample_action")
        {
            this.Content = content;
        }

        /// <summary>
        /// Gets the message's content.
        /// </summary>
        public string Content { get; }
    }
}
