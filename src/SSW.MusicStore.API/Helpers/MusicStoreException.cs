using System;

namespace SSW.MusicStore.API.Helpers
{
    public class MusicStoreException : Exception
    {
        public MusicStoreException(string message) : base (message)
        {
            
        }
    }
}
