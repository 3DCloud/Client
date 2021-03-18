using System;

namespace ActionCableSharp
{
    /// <summary>
    /// Sets the action name for a method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ActionMethodAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ActionMethodAttribute"/> class.
        /// </summary>
        /// <param name="actionName">Name of the action that will execute this method.</param>
        public ActionMethodAttribute(string actionName)
        {
            this.ActionName = actionName;
        }

        /// <summary>
        /// Gets the name of the action that will execute this method.
        /// </summary>
        public string ActionName { get; }
    }
}
