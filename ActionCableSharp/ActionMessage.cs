using System.Text.Json.Serialization;

namespace ActionCableSharp
{
    /// <summary>
    /// Encapsulates an Action Cable action that should be performed on the server.
    /// </summary>
    public abstract class ActionMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActionMessage"/> class.
        /// </summary>
        /// <param name="actionName">The name of the method to execute on the server.</param>
        public ActionMessage(string actionName)
        {
            this.ActionName = actionName;
        }

        /// <summary>
        /// Gets the name of the method executed on the server.
        /// </summary>
        [JsonPropertyName("action")]
        public string ActionName { get; }
    }
}
