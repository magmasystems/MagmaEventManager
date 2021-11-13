using System;

namespace MagmaEventManager
{
    /// <summary>
    /// EventSubscriberAttribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class EventSubscriberAttribute : Attribute
    {
        /// <summary>
        /// EventSubscriberAttribute
        /// </summary>
        /// <param name="topicName">The name of the event</param>
        public EventSubscriberAttribute(string topicName)
        {
            this.Topic = topicName;
            this.CommandName = null;
            this.IsBackground = false;
        }

        /// <summary>
        /// This is the topic that is being subscribes to. The topic can contain the wildcard character ('*') at any
        /// position inthe string. Example topics are "Viper.Command.Foo" or "Viper.Prices.*" or "Viper.*.Inserted".
        /// </summary>
        public string Topic { get; set; }

        /// <summary>
        /// This is the name of a command that is associated withthe subscriber. The CommandName can be used in a
        /// context (right-click) menu for the view that is currently in focus.
        /// </summary>
        public string CommandName { get; set; }

        /// <summary>
        /// True if the subscriber should be invoked asynchronously by the EventManager (using BeginInvoke instead of Invoke)
        /// </summary>
        public bool IsBackground { get; set; }
    }
}