using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyEXR
{
    public sealed class ExrMultipartImage
    {
        public ExrMultipartImage(IEnumerable<ExrImage> images)
        {
            Images = images?.ToList() ?? throw new ArgumentNullException(nameof(images));
        }

        public IList<ExrImage> Images { get; }
    }
}
