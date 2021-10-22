namespace Print3DCloud.Client.ActionCable
{
    /// <summary>
    /// Type of print event.
    /// </summary>
    public enum PrintEventType
    {
        /// <summary>
        /// The print file is being downloaded.
        /// </summary>
        Downloading,

        /// <summary>
        /// The print is running.
        /// </summary>
        Running,

        /// <summary>
        /// The print has succeeded.
        /// </summary>
        Success,

        /// <summary>
        /// The print has errored.
        /// </summary>
        Errored,

        /// <summary>
        /// The print has been canceled.
        /// </summary>
        Canceled,
    }
}