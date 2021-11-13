using System;

namespace MagmaEventManager
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