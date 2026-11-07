namespace TinyEXR
{
    /// <summary>Reusable codec storage for sequential reader and writer lifetimes.</summary>
    /// <remarks>One reader or writer may hold this context until it is disposed. Limits belong to options.</remarks>
    public sealed class ExrContext : IDisposable
    {
        private readonly object _gate = new object();
        private CodecResources? _resources;
        private bool _leased;
        private bool _disposed;

        internal CodecResources Acquire()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ExrContext));
                EnsureIdle();
                CodecResources resources = _resources ??= new CodecResources();
                _leased = true;
                return resources;
            }
        }

        internal void Release()
        {
            lock (_gate) _leased = false;
        }

        /// <summary>Releases retained storage when no reader or writer is using it.</summary>
        public void Trim()
        {
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ExrContext));
                EnsureIdle();
                _resources?.Dispose();
                _resources = null;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                EnsureIdle();
                _resources?.Dispose();
                _resources = null;
                _disposed = true;
            }
        }

        private void EnsureIdle()
        {
            if (_leased) throw new InvalidOperationException("The EXR context is in use by a reader or writer.");
        }
    }
}
