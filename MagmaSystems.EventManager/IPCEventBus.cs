namespace MagmaSystems.EventManager
{
#if USE_IPC
    internal class IPCEventBus
    {
        protected IPCPublisher Publisher { get; set; }
        protected IPCSubscriber Subscriber { get; set; }
        protected EventManager EventManager { get; set; }

        private const string IPCChannelName = "CitsecEventManagerEvents";

        public IPCEventBus(EventManager eventManager)
        {
            this.EventManager = eventManager;

            try
            {
                this.Publisher = new IPCPublisher(IPCChannelName);
                this.Publisher.MessageReceived += this.OnMessageReceived;

                this.Subscriber = new IPCSubscriber(IPCChannelName);
                this.Subscriber.MessageReceived += this.OnMessageReceived;
            }
            catch (Exception exc)
            {
                Console.WriteLine("EventManager IPC exception [" + exc.Message + "]");
            }
        }

        void OnMessageReceived(object sender, IPCMessageEventArgs args)
        {
            IMessage msg = args.Message;
            if (msg == null)
                return;

            if (msg.PayloadType != typeof(EventManagerEventArgs))
                return;

            EventManagerEventArgs eventManagerArgs = msg.Payload as EventManagerEventArgs;
            if (eventManagerArgs == null)
                return;

            // If the EventManager is disabled, then discard the message instead of queueing it up.
            if (!this.EventManager.Enabled)
                return;

            // NOTE - we might want to check to see if the sender of the original message is us. If so,
            // then we may want to disregard the message.

            // We need to send this topic and args back to the EventManager
            try
            {
                EventManager.Publish(this, eventManagerArgs.Topic, eventManagerArgs);
            }
            catch (Exception exc)
            {
                // We may have a non-serializable type
                Console.WriteLine("EventManager IPC publishing exception [" + exc.Message + "]");
            }
        }

        public void Publish(object sender, string topic, EventArgs args)
        {
            if (this.Publisher == null)
                return;

            EventManagerEventArgs eventArgs = new EventManagerEventArgs(topic, args);
            this.Publisher.PublishMessage(eventArgs);
        }


        [Serializable]
        public class EventManagerEventArgs : EventArgs
        {
            public string Topic { get; set; }
            public EventArgs Args { get; set; }

            public EventManagerEventArgs(string topic, EventArgs args)
            {
                this.Topic = topic;
                this.Args = args;
            }
        }
    }
#endif
}