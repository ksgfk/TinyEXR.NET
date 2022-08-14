using System;

namespace TinyEXR
{
    public class TinyExrException : Exception
    {
        public TinyExrException() { }
        public TinyExrException(string message) : base(message) { }
        public TinyExrException(string message, Exception innerException) : base(message, innerException) { }
        public TinyExrException(Exception innerException) : base("managed exception", innerException) { }
    }
}
