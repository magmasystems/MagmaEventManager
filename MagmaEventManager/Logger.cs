using System;

namespace MagmaEventManager
{
    public interface ILogger
    {
        void LogError(string msg, Exception exc = null);
    }
    
    public class Logger : ILogger
    {
        public void LogError(string msg, Exception exc = null)
        {
            Console.WriteLine(msg);
            if (exc != null)
            {
                Console.WriteLine(exc.Message);
            }
        }
    }
}