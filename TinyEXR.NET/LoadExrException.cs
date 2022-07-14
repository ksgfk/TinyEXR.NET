using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace TinyEXR.NET
{
    public class LoadExrException : Exception
    {
        public LoadExrException() { }

        public LoadExrException(string message) : base(message) { }

        public LoadExrException(string message, Exception innerException) : base(message, innerException) { }
    }
}
