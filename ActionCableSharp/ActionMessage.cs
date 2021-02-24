using System.Text.Json.Serialization;

namespace ActionCableSharp
{
    public abstract class ActionMessage
    {
        [JsonPropertyName("action")]
        public string ActionName { get; }

        public ActionMessage(string actionName)
        {
            ActionName = actionName;
        }
    }
}
