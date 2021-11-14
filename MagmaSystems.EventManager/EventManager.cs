using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace MagmaSystems.EventManager
{
    /*
    * This is what happens when a wildcard subscriber gets added
    * Let's say that class A, function foo and class B, function baz both subscribe to "MenuChosen.File.*"
    *
    * EventManager.EventDictionary["MenuChosen.File.*"]
    *   EventManagerEventInfo
    *   	 Publishers = null
    *   	 Subscribers
    *   		 ObjRef=Instance of class A, methodinfo=foo(), topic="MenuChosen.File.*",hasWildcard=true, regex=the regex
    *   		 ObjRef=Instance of class B, methodinfo=baz(), topic="MenuChosen.File.*",hasWildcard=true, regex=the regex
    *
    * EventManager.WildcardDictionary["MenuChosen.File.*"] = EventManager.EventDictionary["MenuChosen.File.*"]
    */

    public class EventManager
    {
        #region Delegates

        // Internal delegate used to help us fire async events
        private delegate void AsyncFire(Delegate del, object[] args);

        public delegate bool EventManagerEventHandler(object sender, string topicName, EventArgs e);

        public delegate bool EventManagerEventHandler<in TEventArgs>(object sender, string topicName, TEventArgs e)
            where TEventArgs : EventArgs;

        #endregion

        #region Variables

        // The singleton EventManager
        private static EventManager m_theEventManager;

#if USE_IPC
    internal static IPCEventBus IPCEventBus { get; set; }
#endif

        #endregion

        #region Constructors and Static Instance

        private EventManager()
        {
            this.Enabled = true;

            Logger = new Logger();
        }

        // This is the way that the EventManager singleton is accessed
        private static EventManager Instance =>
            m_theEventManager ??= new EventManager
            {
                Dictionary = new Dictionary<string, EventManagerEventInfo>(),
                WildcardDictionary = new Dictionary<string, EventManagerEventInfo>(),
                Enabled = true
            };

        #endregion

        #region Properties

        // A Dictionary of EventManagerEventInfo classes, indexed by the topic name
        // make sure that the dictionary and event manager are instantiated
        private Dictionary<string, EventManagerEventInfo> Dictionary { get; set; }

        // make sure that the dictionary and event manager are instantiated
        private Dictionary<string, EventManagerEventInfo> WildcardDictionary { get; set; }

        // Used to disabled and re-enable the firing of events
        public bool Enabled { get; set; }
        
        public static ILogger Logger { get; set; }

        #endregion

        #region General Methods

        public static void Register(object o)
        {
            var type = o.GetType();

            // Go through all of the methods (both public and non-public) and see which ones are publishers and subscribers.
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                          BindingFlags.NonPublic | BindingFlags.Static);

            foreach (var mi in methods)
            {
                //    	if(mi.DeclaringType != type)
                //      	continue;
                foreach (var oAttr in mi.GetCustomAttributes(true))
                {
                    switch (oAttr)
                    {
                        case EventSubscriberAttribute evSubAttr:
                            AddSubscriber(evSubAttr.Topic, o, evSubAttr.IsBackground, mi);
                            break;
                        case EventPublisherAttribute evPubAttr:
                            AddPublisher(evPubAttr.Topic, evPubAttr.Scope, o);
                            break;
                    }
                }
            }
        }

        public static void Unregister(object o)
        {
            if (o == null)
                return;

            // Go through all of the publishers and subscribers in the dictionary, and remove all references to the object
            foreach (var (_, value) in Instance.Dictionary)
            {
                // ADLER NOTE: If we remove an entry while enumerating, we get an exception

                foreach (var pub in value.Publishers.Where(pub => pub.Publisher == o))
                    value.Publishers.Remove(pub);

                foreach (var sub in value.Subscribers.Where(sub => sub.Subscriber == o))
                    value.Subscribers.Remove(sub);
            }

            foreach (var (_, value) in Instance.WildcardDictionary)
            {
                foreach (var sub in value.Subscribers.Where(sub => sub.Subscriber == o))
                    value.Subscribers.Remove(sub);
            }
        }

        private static EventManagerEventInfo FindTopicEntry(string topicName, bool createIfEmpty)
        {
            topicName = topicName.ToUpper();

            if (!Instance.Dictionary.ContainsKey(topicName))
            {
                if (createIfEmpty)
                {
                    // Allocate a new entry
                    var eventInfo = new EventManagerEventInfo();
                    Instance.Dictionary[topicName] = eventInfo;
                    return eventInfo;
                }

                return null;
            }

            // Use the existing entry
            return Instance.Dictionary[topicName];
        }

        #endregion

        #region Adding/Removing a Publisher/Subscriber at run time

        public static void AddPublisher(string topic, object oPublisher)
        {
            AddPublisher(topic, EventScope.Global, oPublisher);
        }

        public static void AddPublisher(string topic, EventScope scope, object oPublisher)
        {
            if (string.IsNullOrEmpty(topic))
                return;

            // See if the topic is already being published. If not, then automatically create it.
            var evInfo = FindTopicEntry(topic, true);
            evInfo?.AddPublisher(topic, scope, oPublisher);
        }

        public static void AddSubscriber(string topic, object oSubscriber, bool isBackground, Delegate del)
        {
            AddSubscriber(topic, oSubscriber, isBackground, del.Method);
        }

        public static void AddSubscriber(string topic, object oSubscriber, bool isBackground, MethodInfo mi)
        {
            if (string.IsNullOrEmpty(topic))
                return;

            // If there is a static function that subscribes to a topic, then only let there be the one static subscriber.
            if (mi.IsStatic && FindTopicEntry(topic, false) != null)
            {
                return;
            }

            // See if the topic is already being subscribed to. If not, then automatically create it.
            var evInfo = FindTopicEntry(topic, true);
            if (evInfo != null)
            {
                var subInfo = evInfo.AddSubscriber(topic, oSubscriber, isBackground, mi);
                if (subInfo.HasWildcards)
                {
                    Instance.WildcardDictionary[topic] = evInfo;
                }
            }
        }

        public static void RemovePublisher(string topic, object oPublisher)
        {
            if (oPublisher == null)
                return;

            if (string.IsNullOrEmpty(topic))
                return;

            var evInfo = FindTopicEntry(topic, false);
            evInfo?.RemovePublisher(topic, oPublisher);
        }

        public static void RemoveSubscriber(string topic, object oSubscriber)
        {
            if (string.IsNullOrEmpty(topic) || oSubscriber == null)
                return;

            var evInfo = FindTopicEntry(topic, false);
            evInfo?.RemoveSubscriber(topic, oSubscriber);
        }

        #endregion

        #region Ways to Fire and Event

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void Publish(object sender, string topicName, EventArgs args)
        {
            if (Instance.Enabled == false)
                return;

            var evInfo = FindTopicEntry(topicName, false);
            EventPublisherInfo publisher = null;
            // In case a topic name is something like RIO.*.*.RECEIVED, it will be found when search the
            // wildcarded topics. So, don't try to fire the event here .. do it below.
            if (topicName.IndexOf('*') < 0 && (evInfo = FindTopicEntry(topicName, false)) != null)
            {
                if (evInfo.Publishers.Count > 0)
                    publisher = evInfo.Publishers[0];
                evInfo.Fire(publisher, sender, topicName, args);
            }

            // Replace the dots with slashes so that the dot is not taken as a regexp
            //	character by the pattern matcher.
            topicName = topicName.Replace('.', '/').ToUpper();

            foreach (var subTopic in Instance.WildcardDictionary.Keys)
            {
                evInfo = Instance.WildcardDictionary[subTopic];
                var regExp = evInfo.Regex;
                if (regExp != null && regExp.IsMatch(topicName))
                {
                    evInfo.Fire(publisher, sender, topicName, args);
                }
            }
        }

        #endregion

        #region Inner Class for EventManagerEventInfo

        /// <summary>
        /// EventManagerEventInfo
        /// This class represents information about a single event
        /// </summary>
        public class EventManagerEventInfo
        {
            #region Variables

            // Multicast delegate of all subcribers to this event
            private event EventManagerEventHandler m_eventHandler;

            // The list of classes that publish this event
            // The list of classes that subscribe to this event

            // In case this is a wildcarded topic

            #endregion

            #region Constructors

            public EventManagerEventInfo()
            {
                Subscribers = new List<EventSubscriberInfo>();
                Publishers = new List<EventPublisherInfo>();
            }

            #endregion

            #region Events

            public event EventManagerEventHandler EventManagerEvent
            {
                add => this.m_eventHandler += value;
                remove => this.m_eventHandler -= value;
            }

            #endregion

            #region Properties

            public EventManagerEventHandler EventHandler => this.m_eventHandler;

            public List<EventPublisherInfo> Publishers { get; private set; }

            public List<EventSubscriberInfo> Subscribers { get; private set; }

            public Regex Regex { get; private set; }

            #endregion

            #region Methods

            public EventSubscriberInfo AddSubscriber(string topicName, object objectRef, bool isBackground,
                MethodInfo mi)
            {
                var subInfo = new EventSubscriberInfo(this, topicName, objectRef, isBackground, mi);
                this.m_eventHandler += subInfo.OnPublisherFired;
                this.Subscribers.Add(subInfo);

                // If we add a wildcarded topic, then precompile the regular expression
                if (subInfo.HasWildcards)
                {
                    var sTopic =
                        subInfo.TopicName.Replace("*", ".*"); // replace plain '*' with '.*' (one or more characters)
                    this.Regex = new Regex(sTopic, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                return subInfo;
            }

            public EventPublisherInfo AddPublisher(string topicName, EventScope scope, object objectRef)
            {
                var pubInfo = new EventPublisherInfo(this, topicName, scope, objectRef);
                this.Publishers.Add(pubInfo);
                return pubInfo;
            }

            public void RemoveSubscriber(string topic, object oSubscriber)
            {
                foreach (var sub in this.Subscribers.Where(sub => sub.Subscriber == oSubscriber))
                {
                    this.RemoveSubscriber(sub);
                    break;
                }
            }

            public void RemoveSubscriber(EventSubscriberInfo sub)
            {
                this.Subscribers.Remove(sub);
            }

            public void RemovePublisher(string topic, object oPublisher)
            {
                foreach (var pub in this.Publishers.Where(pub => pub.Publisher == oPublisher))
                {
                    this.RemovePublisher(pub);
                    break;
                }
            }

            public void RemovePublisher(EventPublisherInfo pub)
            {
                this.Publishers.Remove(pub);
            }

            #endregion

            #region Event Firing

            public void Fire(EventPublisherInfo publisher, object sender, string topicName, EventArgs args)
            {
                // This handles the event firing to the SubscriberInfo class. In turn, the
                // SubscriberInfo object will invoke the actual delegate. The SubscriberInfo
                // object will determine whether the delegate shoul dbe called synchronously
                // or asynchronously.

                //if (this.EventHandler != null)
                //{
                //	this.EventHandler(sender, topicName, args);
                //}

                if (this.m_eventHandler == null)
                    return;

                // Get some info about this publisher. The publisher can be null if the app is doing some ad-hoc publishing
                // and has not registered itself propely with the event manager.
                if (publisher != null && !publisher.Publisher.IsAlive)
                    return;

#if USE_IPC
            // If the args are EventmanagerEventArgs, then we know that the message was one that
            // was received from the IPCEventBus. So, just broadcast it the regular way.
            if (args is IPCEventBus.EventManagerEventArgs)
            {
                this.m_eventHandler(sender, topicName, ((IPCEventBus.EventManagerEventArgs)args).Args);
                return;
            }

            if (publisher != null && publisher.Scope == EventScope.IPC)
            {
                if (IPCEventBus == null)
                {
                    IPCEventBus = new IPCEventBus(Instance);
                }

                IPCEventBus.Publish(sender, topicName.Replace('/', '.'), args);
                return;
            }
#endif

                // If the publisher wants to send the event to everyone, then just use the
                // regular old .NET event mechanism.
                if (publisher == null || (publisher.Scope == EventScope.Global))
                {
                    this.m_eventHandler(sender, topicName, args);
                    return;
                }

                var thePublishersAssembly = publisher.Publisher.Target?.GetType().Assembly;

                // Now we know that the publisher wants to fire the event only to subscribers that are
                // in the same assembly. We need to go through all of the subscribers and see which
                // ones are in the same assembly.
                foreach (var del in this.m_eventHandler.GetInvocationList())
                {
                    // Get the subscriber info
                    if (del.Target is EventSubscriberInfo subInfo &&
                        subInfo.Subscriber.GetType().Assembly == thePublishersAssembly)
                    {
                        // Fire the event to this subscriber
                        del.DynamicInvoke(sender, topicName, args);
                    }
                }
            }

            #endregion
        }

        #endregion

        #region Inner class for EventPublisherInfo

        public class EventPublisherInfo
        {
            #region Variables

            private EventManagerEventInfo m_evInfo;
            private WeakReference m_objectRef;
            private string m_topicName; // the upper-cased name

            #endregion

            #region Constructors

            private EventPublisherInfo()
            {
                this.m_objectRef = null;
            }

            private EventPublisherInfo(EventManagerEventInfo evInfo, string topicName, EventScope scope) : this()
            {
                this.m_evInfo = evInfo;
                this.DisplayName = topicName;
                this.m_topicName = topicName.ToUpper();
                this.Scope = scope;
            }

            public EventPublisherInfo(EventManagerEventInfo evInfo, string topicName, EventScope scope,
                object objectRef) : this(evInfo, topicName, scope)
            {
                this.m_objectRef = new WeakReference(objectRef);
            }

            #endregion

            #region Properties

            public WeakReference Publisher
            {
                get => (this.m_objectRef.IsAlive) ? this.m_objectRef : null;
                set => this.m_objectRef = value;
            }

            private string DisplayName { get; set; }
            public EventScope Scope { get; }

            #endregion
        }

        #endregion

        #region Inner class for EventSubscriberInfo

        public class EventSubscriberInfo
        {
            #region Variables

            private readonly EventManagerEventInfo m_eventInfo; // ref back to the holding container
            private readonly WeakReference m_objectRef;
            private readonly MethodInfo m_methodInfo; // The method that the event firer should Invoke

            //private EventManagerEventHandler m_delegateForAsync;
            private readonly Delegate m_delegateForAsync;

            private string m_topicName; // we may have wildcards
            private bool m_hasWildcards; // to help determine whether to use RegEx or not

            #endregion

            #region Constructors

            private EventSubscriberInfo()
            {
                this.m_objectRef = null;
                this.IsBackground = false;
                this.m_hasWildcards = false;
            }

            private EventSubscriberInfo(EventManagerEventInfo evInfo, string topicName)
                : this()
            {
                this.m_eventInfo = evInfo;
                this.TopicName = topicName; // use the property so that formatting and regex processing is done
            }

            private EventSubscriberInfo(EventManagerEventInfo evInfo, string topicName, object objectRef)
                : this(evInfo, topicName)
            {
                this.m_objectRef = new WeakReference(objectRef);
            }

            public EventSubscriberInfo(EventManagerEventInfo evInfo, string topicName, object objectRef,
                bool isBackground, MethodInfo mi)
                : this(evInfo, topicName, objectRef)
            {
                this.IsBackground = isBackground;
                this.m_methodInfo = mi;

                if (isBackground)
                {
                    var paramInfo = mi.GetParameters();
                    if (paramInfo.Length != 3)
                    {
                        var msg = $"Background Subscriber {mi.Name} must have 3 arguments";
                        Logger.LogError(msg);
                        return;
                    }

                    var piEventArgs = paramInfo[2];
                    if (!typeof(EventArgs).IsAssignableFrom(piEventArgs.ParameterType))
                    {
                        var msg = $"Background Subscriber {mi.Name} must have 3rd argument assignable from EventArgs";
                        Logger.LogError(msg);

                        return;
                    }

                    var handlerEventArgsType =
                        typeof(EventManagerEventHandler<>).MakeGenericType(piEventArgs.ParameterType);
                    //this.m_delegateForAsync = (EventManagerEventHandler)Delegate.CreateDelegate(typeof(EventManagerEventHandler), objectRef, mi.Text);
                    this.m_delegateForAsync = Delegate.CreateDelegate(handlerEventArgsType, objectRef, mi.Name);
                }
            }

            #endregion

            #region Properties

            public object Subscriber => (this.m_objectRef.IsAlive) ? this.m_objectRef.Target : null;

            public bool IsBackground { get; set; }

            public string TopicName
            {
                get => this.m_topicName;
                set => this.m_topicName = FormatTopicName(value);
            }

            public bool HasWildcards => this.m_hasWildcards;

            #endregion

            #region Methods

            private string FormatTopicName(string topic)
            {
                // We want to make things easy for matching publishers and sunscribers.
                // 1) We always use upper-case topic names.
                // 2) Replace the dots with slashes so that the dot is not taken as a regexp
                //	character by the pattern matcher.
                // 3) Let us know whether this topic has a wildcard in it so we know whether
                //	to use the slow regexp matcher or the faster Equals operator.
                topic = topic.Replace('.', '/').ToUpper();

                if (topic.IndexOfAny(new[] { '*' }) >= 0)
                {
                    this.m_hasWildcards = true;
                }

                return topic;
            }

            #endregion

            #region Event Firing

            /// <summary>
            /// This gets called whenever the event manager fires an event.
            /// This does the hard work in event firing. It eventually calls
            /// the subscriber's delegate in order to process the event. it also determines
            /// whether the delegate should be called synchronously or asynchronously.
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="topicName"></param>
            /// <param name="args"></param>
            /// <returns></returns>
            //
            public bool OnPublisherFired(object sender, string topicName, EventArgs args)
            {
                // If the object that this subscriber is bound to has been garbage-collected, then
                // remove the subscriber from the EventInfo's subscriber list and return.
                if (this.Subscriber == null)
                {
                    this.m_eventInfo.RemoveSubscriber(this);
                    return true;
                }

                try
                {
                    // Return the topic name to its original form
                    topicName = topicName.Replace('/', '.');

                    if (this.IsBackground)
                    {
                        AsyncFire asyncFire = InvokeDelegate;
                        asyncFire.BeginInvoke(this.m_delegateForAsync, new[] { sender, topicName, args }, Cleanup,
                            null);
                    }
                    else
                    {
                        var oRet = this.m_methodInfo.Invoke(this.Subscriber, new[] { sender, topicName, args });
                        if (oRet is bool ret)
                            return ret;
                    }
                }
                catch (TargetInvocationException tiExc)
                {
                    const string msg = "EventManager had problems invoking the target";
                    Logger.LogError(msg, tiExc);
                    //	throw;
                }
                catch (TargetException tExc)
                {
                    const string msg = "EventManager tried to call an invalid target - perhaps the object was disposed";
                    Logger.LogError(msg, tExc);
                    //  throw;
                }

                return true;
            }

            private static void InvokeDelegate(Delegate del, object[] args)
            {
                if (del.Target is ISynchronizeInvoke { InvokeRequired: true } synchronizer) // Requires thread affinity
                {
                    synchronizer.Invoke(del, args);
                    return;
                }

                // Not requiring thread afinity or invoke is not required
                try
                {
                    del.DynamicInvoke(args);
                }
                catch (ArgumentException argExc)
                {
                    Logger.LogError("EventManager.InvokeDelegate error ", argExc);
                }
            }

            private static void Cleanup(IAsyncResult asyncResult)
            {
                asyncResult.AsyncWaitHandle.Close();
            }

            #endregion
        }

        #endregion
    }
}