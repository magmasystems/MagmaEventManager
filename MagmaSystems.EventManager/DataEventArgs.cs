using System;

namespace MagmaSystems.EventManager
{
    [Serializable]
    public class DataEventArgs<TData> : EventArgs
    {
        public TData Data { get; protected set; }

        public DataEventArgs(TData data)
        {
            this.Data = data;
        }
    }
}