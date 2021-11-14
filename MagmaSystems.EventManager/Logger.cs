using System;
using Microsoft.Extensions.Logging;

namespace MagmaSystems.EventManager
{
    public interface ILogger
    {
        void LogError(string msg, Exception exc = null);
    }
    
    public class Logger : ILogger
    {
        private Microsoft.Extensions.Logging.ILogger m_logger { get; }
        
        public Logger(string category = "MagmaSystems.EventManager")
        {
            var loggerFactory = LoggerFactory.Create(configure =>
            {
                configure.AddSimpleConsole();
            });
            this.m_logger = loggerFactory.CreateLogger(category);
        }
        
        public void LogError(string msg, Exception exc = null)
        {
            this.m_logger.LogError(msg, exc);
        }
    }
}