using ActionCableSharp;

namespace Client
{
    internal class SampleAction : ActionMessage
    {
        public string Content => "some great content";

        public SampleAction() : base("sample_action") { }
    }
}
