using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyEXR
{
    public sealed class ExrMultipartHeader
    {
        public ExrMultipartHeader(IEnumerable<ExrHeader> headers)
        {
            Headers = headers?.ToList() ?? throw new ArgumentNullException(nameof(headers));
        }

        public IList<ExrHeader> Headers { get; }
    }
}
