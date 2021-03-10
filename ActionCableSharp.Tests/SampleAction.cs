using ActionCableSharp;

namespace Client
{
    internal class SampleAction : ActionMessage
    {
        public SampleAction(string content)
            : base("sample_action")
        {
            this.Content = content;
        }

        public string Content { get; }
    }
}
