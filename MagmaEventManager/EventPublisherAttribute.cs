using System;

namespace MagmaEventManager
{
    /// <summary>
    /// EventPublisherAttribute
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class EventPublisherAttribute : Attribute
    {
        /// <summary>
        /// EventPublisherAttribute
        /// </summary>
        /// <param name="topicName">The name of the event</param>
        public EventPublisherAttribute(string topicName)
        {
            this.Topic = topicName;
            this.Scope = EventScope.Global;
        }

        public EventPublisherAttribute(string topicName, EventScope scope) : this(topicName)
        {
            this.Scope = scope;
        }

        public string Topic { get; set; }
        public EventScope Scope { get; set; }
    }
}