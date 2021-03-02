using ActionCableSharp;

namespace Client
{
    internal class SampleAction : ActionMessage
    {
        public string Content { get; }

        public SampleAction(string content) : base("sample_action")
        {
            Content = content;
        }
    }
}
