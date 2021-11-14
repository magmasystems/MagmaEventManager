using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MagmaSystems.EventManager.Tests
{
	/// <summary>
	/// Summary description for EventManagerTest
	/// </summary>
	[TestClass]
	// [Ignore]
	public class EventManagerTest
	{
		#region Additional test attributes
		//
		// You can use the following additional attributes as you write your tests:
		//
		// Use ClassInitialize to run code before running the first test in the class
		// [ClassInitialize()]
		// public static void MyClassInitialize(TestContext testContext) { }
		//
		// Use ClassCleanup to run code after all tests in a class have run
		// [ClassCleanup()]
		// public static void MyClassCleanup() { }
		//
		// Use TestInitialize to run code before running each test 
		// [TestInitialize()]
		// public void MyTestInitialize() { }
		//
		// Use TestCleanup to run code after each test has run
		// [TestCleanup()]
		// public void MyTestCleanup() { }
		//
		#endregion

		public static readonly ManualResetEvent Signal = new ManualResetEvent(false);
	
		[TestMethod]
		public void EventManager_Test()
		{
			#pragma warning disable 168
			var publisher = new EventManagerTestPublisher();
			var subscriber = new EventManagerTestSubscriber();
			#pragma warning restore 168

			publisher.Publish();
			publisher.AdHocPublish();

			Signal.WaitOne();
			Assert.IsTrue(subscriber.NumTopicsReceived >= 3, "Did not receive 3 topics");
		}
	}

	public class EventManagerTestPublisher
	{
		public EventManagerTestPublisher()
		{
			EventManager.Register(this);
		}

		[EventPublisher("EventManagerTest.Topic1", Scope = EventScope.IPC)]
		public void Publish()
		{
			EventManager.Publish(this, "EventManagerTest.Topic1", new DataEventArgs<string>("Hello World"));
		}

		public void AdHocPublish()
		{
			EventManager.Publish(this, "EventManagerTest.Topic2", new DataEventArgs<string>("May the force be with you"));
		}
	}

	public class EventManagerTestSubscriber
	{
		public int NumTopicsReceived;
	
		public EventManagerTestSubscriber()
		{
			EventManager.Register(this);
		}

		[EventSubscriber("EventManagerTest.Topic1")]
		public void ExplicitSubscribe(object sender, string topic, DataEventArgs<string> e)
		{
			this.IncrementEvents();
			Console.WriteLine("Explicit subscriber received " + e.Data);
		}

		[EventSubscriber("EventManagerTest.*")]
		public void WildcardSubscribe(object sender, string topic, DataEventArgs<string> e)
		{
			this.IncrementEvents();
			Console.WriteLine("Wildcard subscriber received " + e.Data);
		}

		private void IncrementEvents()
		{
			Interlocked.Increment(ref this.NumTopicsReceived);
			if (this.NumTopicsReceived >= 3)
				EventManagerTest.Signal.Set();
		}
	}
}