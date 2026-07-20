using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyEXR.PortV1;
using TinyEXR.V3.Codecs;
using TinyEXR.V3.Format;
using TinyEXR.V3.IO;

namespace TinyEXR.V3
{
    /// <summary>
    /// Block-at-a-time OpenEXR writer. Add all part headers, begin the stream, write every
    /// logical block exactly once in any order, and end the stream to backpatch offsets.
    /// </summary>
    public sealed class ExrWriter : IDisposable, IAsyncDisposable
    {
        private readonly ISeekableDataSink? _syncSink;
        private readonly IAsyncSeekableDataSink? _asyncSink;
        private readonly object _sinkOwner;
        private readonly bool _leaveOpen;
        private readonly bool _forceMultipart;
        private readonly WriterLimits _limits;
        private readonly List<Header> _headers = new List<Header>();
        private readonly ExrCompressionCodec.EncodeWorkspace _encodeWorkspace =
            new ExrCompressionCodec.EncodeWorkspace();
        private readonly ZstdCompressionEncoder _zstdEncoder = new ZstdCompressionEncoder();
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly object _stateGate = new object();

        private WriterState _state = WriterState.Created;
        private DataRange? _pending;
        private WriterResult? _terminalResult;
        private BeginOperation? _beginOperation;
        private PendingBlock? _pendingBlock;
        private EndOperation? _endOperation;
        private WriterPartData[]? _parts;
        private bool _multipart;
        private long _streamEndPosition;
        private bool _disposed;

        private ExrWriter(
            ISeekableDataSink? syncSink,
            IAsyncSeekableDataSink? asyncSink,
            object sinkOwner,
            WriterOptions options)
        {
            _syncSink = syncSink;
            _asyncSink = asyncSink;
            _sinkOwner = sinkOwner;
            _leaveOpen = options.LeaveOpen;
            _forceMultipart = options.ForceMultipart;
            _limits = options.Limits;
        }

        public WriterState State
        {
            get
            {
                lock (_stateGate)
                {
                    return _state;
                }
            }
        }

        /// <summary>
        /// Optional retry information from the most recent WouldBlock result.
        /// </summary>
        public DataRange? Pending
        {
            get
            {
                lock (_stateGate)
                {
                    return _pending;
                }
            }
        }

        public int NumParts
        {
            get
            {
                lock (_stateGate)
                {
                    ThrowIfDisposedLocked();
                    return _headers.Count;
                }
            }
        }

        public static ExrWriter OpenSink(
            ISeekableDataSink sink,
            WriterOptions? options = null)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            return new ExrWriter(
                sink,
                sink as IAsyncSeekableDataSink,
                sink,
                options ?? new WriterOptions());
        }

        public static ExrWriter OpenAsyncSink(
            IAsyncSeekableDataSink sink,
            WriterOptions? options = null)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            return new ExrWriter(
                sink as ISeekableDataSink,
                sink,
                sink,
                options ?? new WriterOptions());
        }

        /// <summary>
        /// Adds an immutable part description. All parts must be added before Begin.
        /// </summary>
        public int AddPart(Header header)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            _operationGate.Wait();
            try
            {
                lock (_stateGate)
                {
                    ThrowIfDisposedLocked();
                    if (_state != WriterState.Created)
                    {
                        throw new InvalidOperationException("Parts cannot be added after Begin has started.");
                    }

                    if (_headers.Count >= _limits.MaximumParts)
                    {
                        throw new WriterLimitExceededException(
                            nameof(_limits.MaximumParts),
                            _headers.Count + 1L,
                            _limits.MaximumParts);
                    }

                    int partIndex = _headers.Count;
                    _headers.Add(header);
                    return partIndex;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public Header GetHeader(int partIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            lock (_stateGate)
            {
                ThrowIfDisposedLocked();
                if (partIndex >= _headers.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(partIndex));
                }

                return _headers[partIndex];
            }
        }

        public int GetNumBlocks(int partIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            lock (_stateGate)
            {
                ThrowIfDisposedLocked();
                if (partIndex >= _headers.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(partIndex));
                }

                if (_parts != null)
                {
                    return _parts[partIndex].Layout.BlockCount;
                }

                return ExrFormatParser.ComputeChunkCount(_headers[partIndex]);
            }
        }

        public BlockInfo GetBlockInfo(int partIndex, int blockIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            lock (_stateGate)
            {
                ThrowIfDisposedLocked();
                if (_parts == null)
                {
                    throw new InvalidOperationException("Block layout is available only after Begin completes.");
                }

                if (partIndex >= _parts.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(partIndex));
                }

                WriterPartData part = _parts[partIndex];
                if (blockIndex >= part.Layout.BlockCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(blockIndex));
                }

                ulong offset = part.Offsets[blockIndex];
                return part.Layout.GetBlockInfo(
                    partIndex,
                    blockIndex,
                    offset == 0 ? 0L : checked((long)offset));
            }
        }

        /// <summary>
        /// Writes magic, version, part headers, and zeroed chunk offset tables.
        /// An exact partial I/O failure can be retried by calling Begin again.
        /// </summary>
        public WriterResult Begin()
        {
            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with an asynchronous-only sink.");
                }

                return Publish(BeginCore());
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async ValueTask<WriterResult> BeginAsync(CancellationToken cancellationToken = default)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with a synchronous-only sink.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriterResult result = await BeginCoreAsync(cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public WriterResult WriteScanlineBlock(
            int partIndex,
            int minimumY,
            IReadOnlyList<ChannelBuffer> channels)
        {
            ValidatePartIndex(partIndex);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with an asynchronous-only sink.");
                }

                return Publish(WriteBlockCore(
                    partIndex,
                    minimumY,
                    tileX: -1,
                    tileY: -1,
                    levelX: 0,
                    levelY: 0,
                    channels));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async ValueTask<WriterResult> WriteScanlineBlockAsync(
            int partIndex,
            int minimumY,
            IReadOnlyList<ChannelBuffer> channels,
            CancellationToken cancellationToken = default)
        {
            ValidatePartIndex(partIndex);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with a synchronous-only sink.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriterResult result = await WriteBlockCoreAsync(
                    partIndex,
                    minimumY,
                    tileX: -1,
                    tileY: -1,
                    levelX: 0,
                    levelY: 0,
                    channels,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public WriterResult WriteTile(
            int partIndex,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            IReadOnlyList<ChannelBuffer> channels)
        {
            ValidateTileArguments(partIndex, tileX, tileY, levelX, levelY);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with an asynchronous-only sink.");
                }

                return Publish(WriteBlockCore(
                    partIndex,
                    minimumY: 0,
                    tileX,
                    tileY,
                    levelX,
                    levelY,
                    channels));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async ValueTask<WriterResult> WriteTileAsync(
            int partIndex,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            IReadOnlyList<ChannelBuffer> channels,
            CancellationToken cancellationToken = default)
        {
            ValidateTileArguments(partIndex, tileX, tileY, levelX, levelY);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with a synchronous-only sink.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriterResult result = await WriteBlockCoreAsync(
                    partIndex,
                    minimumY: 0,
                    tileX,
                    tileY,
                    levelX,
                    levelY,
                    channels,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Writes one deep scanline block from per-pixel sample counts and channel-planar samples.
        /// </summary>
        public WriterResult WriteDeepScanlineBlock(
            int partIndex,
            int minimumY,
            ReadOnlySpan<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels)
        {
            ValidatePartIndex(partIndex);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with an asynchronous-only sink.");
                }

                return Publish(WriteDeepBlockCore(
                    partIndex,
                    minimumY,
                    tileX: -1,
                    tileY: -1,
                    levelX: 0,
                    levelY: 0,
                    sampleCounts,
                    channels));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>Asynchronously writes one deep scanline block.</summary>
        public async ValueTask<WriterResult> WriteDeepScanlineBlockAsync(
            int partIndex,
            int minimumY,
            ReadOnlyMemory<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels,
            CancellationToken cancellationToken = default)
        {
            ValidatePartIndex(partIndex);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with a synchronous-only sink.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriterResult result = await WriteDeepBlockCoreAsync(
                    partIndex,
                    minimumY,
                    tileX: -1,
                    tileY: -1,
                    levelX: 0,
                    levelY: 0,
                    sampleCounts,
                    channels,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Writes one deep tile from per-pixel sample counts and channel-planar samples.
        /// </summary>
        public WriterResult WriteDeepTile(
            int partIndex,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            ReadOnlySpan<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels)
        {
            ValidateTileArguments(partIndex, tileX, tileY, levelX, levelY);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with an asynchronous-only sink.");
                }

                return Publish(WriteDeepBlockCore(
                    partIndex,
                    minimumY: 0,
                    tileX,
                    tileY,
                    levelX,
                    levelY,
                    sampleCounts,
                    channels));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>Asynchronously writes one deep tile.</summary>
        public async ValueTask<WriterResult> WriteDeepTileAsync(
            int partIndex,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            ReadOnlyMemory<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels,
            CancellationToken cancellationToken = default)
        {
            ValidateTileArguments(partIndex, tileX, tileY, levelX, levelY);
            if (channels == null)
            {
                throw new ArgumentNullException(nameof(channels));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with a synchronous-only sink.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriterResult result = await WriteDeepBlockCoreAsync(
                    partIndex,
                    minimumY: 0,
                    tileX,
                    tileY,
                    levelX,
                    levelY,
                    sampleCounts,
                    channels,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Backpatches every offset table, restores the sink to the end of pixel data, and flushes.
        /// </summary>
        public WriterResult End()
        {
            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with an asynchronous-only sink.");
                }

                return Publish(EndCore());
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async ValueTask<WriterResult> EndAsync(CancellationToken cancellationToken = default)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSink == null)
                {
                    throw new NotSupportedException("This writer was opened with a synchronous-only sink.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                WriterResult result = await EndCoreAsync(cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public void Dispose()
        {
            _operationGate.Wait();
            try
            {
                if (_disposed)
                {
                    return;
                }

                SetDisposed();
                _zstdEncoder.Dispose();
                if (!_leaveOpen)
                {
                    if (_sinkOwner is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    else if (_sinkOwner is IAsyncDisposable asyncDisposable)
                    {
                        asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    }
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                {
                    return;
                }

                SetDisposed();
                _zstdEncoder.Dispose();
                if (!_leaveOpen)
                {
                    if (_sinkOwner is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else if (_sinkOwner is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private WriterResult BeginCore()
        {
            WriterResult? setup = PrepareBegin();
            if (setup.HasValue)
            {
                return setup.Value;
            }

            long bytesWritten = 0;
            WriterResult? writeResult = DrainOutput(_beginOperation!.Output, ref bytesWritten);
            if (writeResult.HasValue)
            {
                return WithBytes(writeResult.Value, bytesWritten);
            }

            CompleteBegin();
            return Success(bytesWritten);
        }

        private async ValueTask<WriterResult> BeginCoreAsync(CancellationToken cancellationToken)
        {
            WriterResult? setup = PrepareBegin();
            if (setup.HasValue)
            {
                return setup.Value;
            }

            long bytesWritten = 0;
            WriterResult? writeResult = await DrainOutputAsync(
                _beginOperation!.Output,
                cancellationToken).ConfigureAwait(false);
            bytesWritten = _beginOperation.Output.InvocationBytesWritten;
            if (writeResult.HasValue)
            {
                return WithBytes(writeResult.Value, bytesWritten);
            }

            CompleteBegin();
            return Success(bytesWritten);
        }

        private WriterResult? PrepareBegin()
        {
            WriterState state = State;
            if (state == WriterState.Faulted)
            {
                return _terminalResult ?? IoFailure(new IOException("The writer is faulted."));
            }

            if (state == WriterState.Beginning)
            {
                return null;
            }

            if (state != WriterState.Created)
            {
                return Invalid("Begin is valid only before streaming starts.");
            }

            try
            {
                WriterFilePlan plan = WriterPlanBuilder.Build(_headers, _limits, _forceMultipart);
                _beginOperation = new BeginOperation(plan);
                SetState(WriterState.Beginning);
                return null;
            }
            catch (WriterPlanException exception)
            {
                return new WriterResult(
                    exception.Result,
                    null,
                    exception.InnerException ?? exception);
            }
            catch (OutOfMemoryException exception)
            {
                return new WriterResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                return Invalid("The writer metadata size overflowed the supported address space.", exception);
            }
            catch (ArgumentException exception)
            {
                return Invalid("The writer header is invalid.", exception);
            }
        }

        private void CompleteBegin()
        {
            WriterFilePlan plan = _beginOperation!.Plan;
            _parts = plan.Parts;
            _multipart = plan.Multipart;
            _streamEndPosition = plan.Prefix.LongLength;
            _beginOperation = null;
            SetState(WriterState.Streaming);
        }

        private WriterResult WriteBlockCore(
            int partIndex,
            int minimumY,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            IReadOnlyList<ChannelBuffer> channels)
        {
            WriterResult? setup = PrepareBlock(
                partIndex,
                minimumY,
                tileX,
                tileY,
                levelX,
                levelY,
                channels,
                out PendingBlock? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            long bytesWritten = 0;
            WriterResult? writeResult = DrainOutput(operation!.Output, ref bytesWritten);
            if (writeResult.HasValue)
            {
                return WithBytes(writeResult.Value, bytesWritten);
            }

            CompleteBlock(operation);
            return Success(bytesWritten);
        }

        private WriterResult WriteDeepBlockCore(
            int partIndex,
            int minimumY,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            ReadOnlySpan<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels)
        {
            WriterResult? setup = PrepareDeepBlock(
                partIndex,
                minimumY,
                tileX,
                tileY,
                levelX,
                levelY,
                sampleCounts,
                channels,
                out PendingBlock? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            long bytesWritten = 0;
            WriterResult? writeResult = DrainOutput(operation!.Output, ref bytesWritten);
            if (writeResult.HasValue)
            {
                return WithBytes(writeResult.Value, bytesWritten);
            }

            CompleteBlock(operation);
            return Success(bytesWritten);
        }

        private async ValueTask<WriterResult> WriteDeepBlockCoreAsync(
            int partIndex,
            int minimumY,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            ReadOnlyMemory<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels,
            CancellationToken cancellationToken)
        {
            WriterResult? setup = PrepareDeepBlock(
                partIndex,
                minimumY,
                tileX,
                tileY,
                levelX,
                levelY,
                sampleCounts.Span,
                channels,
                out PendingBlock? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            operation!.Output.InvocationBytesWritten = 0;
            WriterResult? writeResult = await DrainOutputAsync(
                operation.Output,
                cancellationToken).ConfigureAwait(false);
            long bytesWritten = operation.Output.InvocationBytesWritten;
            if (writeResult.HasValue)
            {
                return WithBytes(writeResult.Value, bytesWritten);
            }

            CompleteBlock(operation);
            return Success(bytesWritten);
        }

        private async ValueTask<WriterResult> WriteBlockCoreAsync(
            int partIndex,
            int minimumY,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            IReadOnlyList<ChannelBuffer> channels,
            CancellationToken cancellationToken)
        {
            WriterResult? setup = PrepareBlock(
                partIndex,
                minimumY,
                tileX,
                tileY,
                levelX,
                levelY,
                channels,
                out PendingBlock? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            operation!.Output.InvocationBytesWritten = 0;
            WriterResult? writeResult = await DrainOutputAsync(
                operation.Output,
                cancellationToken).ConfigureAwait(false);
            long bytesWritten = operation.Output.InvocationBytesWritten;
            if (writeResult.HasValue)
            {
                return WithBytes(writeResult.Value, bytesWritten);
            }

            CompleteBlock(operation);
            return Success(bytesWritten);
        }

        private WriterResult? PrepareBlock(
            int partIndex,
            int minimumY,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            IReadOnlyList<ChannelBuffer> channels,
            out PendingBlock? operation)
        {
            operation = null;
            WriterState state = State;
            if (state == WriterState.Faulted)
            {
                return _terminalResult ?? IoFailure(new IOException("The writer is faulted."));
            }

            if (_parts == null || (state != WriterState.Streaming && state != WriterState.WritingBlock))
            {
                return Invalid("Blocks can be written only after Begin and before End.");
            }

            if (partIndex >= _parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            WriterPartData part = _parts[partIndex];
            bool tiledCall = tileX >= 0;
            if (part.Header.IsDeep)
            {
                return Unsupported("Use a deep writer operation for deep parts.");
            }

            if (part.Header.IsTiled != tiledCall)
            {
                return Invalid(
                    part.Header.IsTiled
                        ? "A tiled part requires WriteTile."
                        : "A scanline part requires WriteScanlineBlock.");
            }

            int blockIndex;
            bool found = tiledCall
                ? part.Layout.TryGetTiledBlockIndex(tileX, tileY, levelX, levelY, out blockIndex)
                : part.Layout.TryGetScanlineBlockIndex(minimumY, out blockIndex);
            if (!found)
            {
                return Invalid("The requested block coordinate is not valid for this part.");
            }

            if (_pendingBlock != null)
            {
                if (_pendingBlock.PartIndex != partIndex || _pendingBlock.BlockIndex != blockIndex)
                {
                    return Invalid("Retry the pending block before starting a different block.");
                }

                operation = _pendingBlock;
                return null;
            }

            if (part.Written[blockIndex])
            {
                return Invalid("Every logical block may be written only once.");
            }

            try
            {
                BlockInfo info = part.Layout.GetBlockInfo(partIndex, blockIndex, 0);
                byte[] chunk = FlatBlockEncoder.Encode(
                    part,
                    info,
                    channels,
                    _multipart,
                    _limits,
                    _encodeWorkspace,
                    _zstdEncoder);
                operation = new PendingBlock(
                    partIndex,
                    blockIndex,
                    _streamEndPosition,
                    chunk);
                _pendingBlock = operation;
                SetState(WriterState.WritingBlock);
                return null;
            }
            catch (WriterPlanException exception)
            {
                return new WriterResult(
                    exception.Result,
                    null,
                    exception.InnerException ?? exception);
            }
            catch (OutOfMemoryException exception)
            {
                return new WriterResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                return Invalid("The block geometry or encoded length overflowed.", exception);
            }
            catch (ArgumentException exception)
            {
                return Invalid("The block channel buffers are invalid.", exception);
            }
        }

        private WriterResult? PrepareDeepBlock(
            int partIndex,
            int minimumY,
            int tileX,
            int tileY,
            int levelX,
            int levelY,
            ReadOnlySpan<int> sampleCounts,
            IReadOnlyList<ChannelBuffer> channels,
            out PendingBlock? operation)
        {
            operation = null;
            WriterState state = State;
            if (state == WriterState.Faulted)
            {
                return _terminalResult ?? IoFailure(new IOException("The writer is faulted."));
            }

            if (_parts == null || (state != WriterState.Streaming && state != WriterState.WritingBlock))
            {
                return Invalid("Blocks can be written only after Begin and before End.");
            }

            if (partIndex >= _parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            WriterPartData part = _parts[partIndex];
            bool tiledCall = tileX >= 0;
            if (!part.Header.IsDeep)
            {
                return Unsupported("Use a flat writer operation for flat parts.");
            }

            if (part.Header.IsTiled != tiledCall)
            {
                return Invalid(
                    part.Header.IsTiled
                        ? "A deep tiled part requires WriteDeepTile."
                        : "A deep scanline part requires WriteDeepScanlineBlock.");
            }

            int blockIndex;
            bool found = tiledCall
                ? part.Layout.TryGetTiledBlockIndex(tileX, tileY, levelX, levelY, out blockIndex)
                : part.Layout.TryGetScanlineBlockIndex(minimumY, out blockIndex);
            if (!found)
            {
                return Invalid("The requested block coordinate is not valid for this part.");
            }

            if (_pendingBlock != null)
            {
                if (_pendingBlock.PartIndex != partIndex || _pendingBlock.BlockIndex != blockIndex)
                {
                    return Invalid("Retry the pending block before starting a different block.");
                }

                operation = _pendingBlock;
                return null;
            }

            if (part.Written[blockIndex])
            {
                return Invalid("Every logical block may be written only once.");
            }

            try
            {
                BlockInfo info = part.Layout.GetBlockInfo(partIndex, blockIndex, 0);
                DeepEncodedBlock encoded = DeepBlockEncoder.Encode(
                    part,
                    info,
                    sampleCounts,
                    channels,
                    _multipart,
                    _limits,
                    _encodeWorkspace,
                    _zstdEncoder);
                operation = new PendingBlock(
                    partIndex,
                    blockIndex,
                    _streamEndPosition,
                    encoded.Chunk,
                    encoded.MaximumSamplesPerPixel);
                _pendingBlock = operation;
                SetState(WriterState.WritingBlock);
                return null;
            }
            catch (WriterPlanException exception)
            {
                return new WriterResult(
                    exception.Result,
                    null,
                    exception.InnerException ?? exception);
            }
            catch (OutOfMemoryException exception)
            {
                return new WriterResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                return Invalid("The deep block geometry or encoded length overflowed.", exception);
            }
            catch (ArgumentException exception)
            {
                return Invalid("The deep block buffers are invalid.", exception);
            }
        }

        private void CompleteBlock(PendingBlock operation)
        {
            WriterPartData part = _parts![operation.PartIndex];
            if (part.Written[operation.BlockIndex])
            {
                throw new InvalidOperationException("The pending block was already committed.");
            }

            part.Offsets[operation.BlockIndex] = checked((ulong)operation.StartPosition);
            part.Written[operation.BlockIndex] = true;
            part.MaximumSamplesPerPixel = Math.Max(
                part.MaximumSamplesPerPixel,
                operation.MaximumSamplesPerPixel);
            _streamEndPosition = checked(operation.StartPosition + operation.Output.Data.LongLength);
            _pendingBlock = null;
            SetState(WriterState.Streaming);
        }

        private WriterResult EndCore()
        {
            WriterResult? setup = PrepareEnd();
            if (setup.HasValue)
            {
                return setup.Value;
            }

            long bytesWritten = 0;
            WriterResult? result = AdvanceEnd(ref bytesWritten);
            return result.HasValue ? WithBytes(result.Value, bytesWritten) : Success(bytesWritten);
        }

        private async ValueTask<WriterResult> EndCoreAsync(CancellationToken cancellationToken)
        {
            WriterResult? setup = PrepareEnd();
            if (setup.HasValue)
            {
                return setup.Value;
            }

            long bytesWritten = 0;
            WriterResult? result = await AdvanceEndAsync(cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < _endOperation!.Patches.Length; i++)
            {
                bytesWritten += _endOperation.Patches[i].InvocationBytesWritten;
            }

            if (result.HasValue)
            {
                return WithBytes(result.Value, bytesWritten);
            }

            CompleteEnd();
            return Success(bytesWritten);
        }

        private WriterResult? PrepareEnd()
        {
            WriterState state = State;
            if (state == WriterState.Faulted)
            {
                return _terminalResult ?? IoFailure(new IOException("The writer is faulted."));
            }

            if (state == WriterState.Complete)
            {
                return Success();
            }

            if (state == WriterState.Ending)
            {
                return null;
            }

            if (state == WriterState.WritingBlock)
            {
                return Invalid("Retry the pending block before ending the stream.");
            }

            if (state != WriterState.Streaming || _parts == null)
            {
                return Invalid("End is valid only after Begin completes.");
            }

            for (int partIndex = 0; partIndex < _parts.Length; partIndex++)
            {
                bool[] written = _parts[partIndex].Written;
                for (int blockIndex = 0; blockIndex < written.Length; blockIndex++)
                {
                    if (!written[blockIndex])
                    {
                        return Invalid(
                            $"Part {partIndex} block {blockIndex} has not been written.");
                    }
                }
            }

            try
            {
                List<PendingOutput> patches = new List<PendingOutput>(_parts.Length * 2);
                for (int partIndex = 0; partIndex < _parts.Length; partIndex++)
                {
                    WriterPartData part = _parts[partIndex];
                    byte[] table = new byte[checked(part.Offsets.Length * sizeof(ulong))];
                    for (int blockIndex = 0; blockIndex < part.Offsets.Length; blockIndex++)
                    {
                        BinaryPrimitives.WriteUInt64LittleEndian(
                            table.AsSpan(blockIndex * sizeof(ulong), sizeof(ulong)),
                            part.Offsets[blockIndex]);
                    }

                    patches.Add(new PendingOutput(part.OffsetTablePosition, table));
                }

                for (int partIndex = 0; partIndex < _parts.Length; partIndex++)
                {
                    WriterPartData part = _parts[partIndex];
                    if (part.MaximumSamplesPosition.HasValue)
                    {
                        byte[] maximumSamples = new byte[sizeof(int)];
                        BinaryPrimitives.WriteInt32LittleEndian(
                            maximumSamples,
                            part.MaximumSamplesPerPixel);
                        patches.Add(new PendingOutput(
                            part.MaximumSamplesPosition.Value,
                            maximumSamples));
                    }
                }

                _endOperation = new EndOperation(patches.ToArray());
                SetState(WriterState.Ending);
                return null;
            }
            catch (OutOfMemoryException exception)
            {
                return new WriterResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                return new WriterResult(
                    ExrResult.Unsupported,
                    null,
                    new NotSupportedException(
                        "The chunk offset tables exceed the managed address space.",
                        exception));
            }
        }

        private WriterResult? AdvanceEnd(ref long bytesWritten)
        {
            EndOperation operation = _endOperation!;
            while (operation.PatchIndex < operation.Patches.Length)
            {
                PendingOutput patch = operation.Patches[operation.PatchIndex];
                WriterResult? result = DrainOutput(patch, ref bytesWritten);
                if (result.HasValue)
                {
                    return result;
                }

                operation.PatchIndex++;
            }

            if (!operation.EndPositionRestored)
            {
                WriterResult? seek = SeekSync(_streamEndPosition);
                if (seek.HasValue)
                {
                    return seek;
                }

                operation.EndPositionRestored = true;
            }

            WriterResult? flush = FlushSync();
            if (flush.HasValue)
            {
                return flush;
            }

            CompleteEnd();
            return null;
        }

        private async ValueTask<WriterResult?> AdvanceEndAsync(CancellationToken cancellationToken)
        {
            EndOperation operation = _endOperation!;
            for (int i = 0; i < operation.Patches.Length; i++)
            {
                operation.Patches[i].InvocationBytesWritten = 0;
            }

            while (operation.PatchIndex < operation.Patches.Length)
            {
                PendingOutput patch = operation.Patches[operation.PatchIndex];
                WriterResult? result = await DrainOutputAsync(
                    patch,
                    cancellationToken).ConfigureAwait(false);
                if (result.HasValue)
                {
                    return result;
                }

                operation.PatchIndex++;
            }

            if (!operation.EndPositionRestored)
            {
                WriterResult? seek = await SeekAsync(_streamEndPosition, cancellationToken).ConfigureAwait(false);
                if (seek.HasValue)
                {
                    return seek;
                }

                operation.EndPositionRestored = true;
            }

            return await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        private void CompleteEnd()
        {
            _endOperation = null;
            SetState(WriterState.Complete);
        }

        private WriterResult? DrainOutput(PendingOutput output, ref long bytesWritten)
        {
            output.InvocationBytesWritten = 0;
            if (output.Offset == output.Data.Length)
            {
                return null;
            }

            if (!output.SeekCompleted)
            {
                WriterResult? seek = SeekSync(checked(output.AbsolutePosition + output.Offset));
                if (seek.HasValue)
                {
                    return seek;
                }

                output.SeekCompleted = true;
            }

            int remaining = output.Data.Length - output.Offset;
            DataTransferResult transfer;
            try
            {
                transfer = _syncSink!.Write(output.Data.AsSpan(output.Offset, remaining));
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is OperationCanceledException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return FaultedIo(
                    new IOException("The sink threw without reporting exact write progress.", exception));
            }

            WriterResult? result = HandleWriteTransfer(transfer, output, remaining);
            bytesWritten += output.InvocationBytesWritten;
            return result;
        }

        private async ValueTask<WriterResult?> DrainOutputAsync(
            PendingOutput output,
            CancellationToken cancellationToken)
        {
            output.InvocationBytesWritten = 0;
            if (output.Offset == output.Data.Length)
            {
                return null;
            }

            if (!output.SeekCompleted)
            {
                WriterResult? seek = await SeekAsync(
                    checked(output.AbsolutePosition + output.Offset),
                    cancellationToken).ConfigureAwait(false);
                if (seek.HasValue)
                {
                    return seek;
                }

                output.SeekCompleted = true;
            }

            int remaining = output.Data.Length - output.Offset;
            DataTransferResult transfer;
            try
            {
                transfer = await _asyncSink!.WriteAsync(
                    output.Data.AsMemory(output.Offset, remaining),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                FaultedIo(new IOException(
                    "The sink canceled without reporting exact write progress.",
                    exception));
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return FaultedIo(
                    new IOException("The sink threw without reporting exact write progress.", exception));
            }

            return HandleWriteTransfer(transfer, output, remaining);
        }

        private WriterResult? HandleWriteTransfer(
            DataTransferResult transfer,
            PendingOutput output,
            int requestedLength)
        {
            switch (transfer.Status)
            {
                case DataTransferStatus.Success:
                    if (!transfer.IsByteCountExact || transfer.BytesTransferred != requestedLength)
                    {
                        return FaultedIo(new IOException(
                            "A sink reported write success without the requested exact byte count."));
                    }

                    output.Offset += requestedLength;
                    output.InvocationBytesWritten += requestedLength;
                    return null;
                case DataTransferStatus.WouldBlock:
                    if (!transfer.IsByteCountExact || transfer.BytesTransferred != 0)
                    {
                        return FaultedIo(new IOException(
                            "A blocked sink reported ambiguous write progress."));
                    }

                    output.SeekCompleted = false;
                    return new WriterResult(ExrResult.WouldBlock, transfer.PendingRange, null);
                case DataTransferStatus.Canceled:
                    AcceptExactPartialWrite(transfer, output, requestedLength);
                    throw transfer.Error as OperationCanceledException ?? new OperationCanceledException();
                case DataTransferStatus.Disposed:
                case DataTransferStatus.IoError:
                    if (!TryAcceptExactPartialWrite(transfer, output, requestedLength))
                    {
                        return FaultedIo(transfer.Error ?? new IOException(
                            "The sink failed without reporting exact write progress."));
                    }

                    return IoFailure(transfer.Error ?? new IOException("The sink write failed."));
                case DataTransferStatus.EndOfSource:
                    return FaultedIo(new IOException("An output sink returned EndOfSource."));
                default:
                    return FaultedIo(new IOException("The output sink returned an unknown status."));
            }
        }

        private void AcceptExactPartialWrite(
            DataTransferResult transfer,
            PendingOutput output,
            int requestedLength)
        {
            if (!TryAcceptExactPartialWrite(transfer, output, requestedLength))
            {
                FaultedIo(new IOException(
                    "The sink canceled without reporting exact write progress.",
                    transfer.Error));
            }
        }

        private static bool TryAcceptExactPartialWrite(
            DataTransferResult transfer,
            PendingOutput output,
            int requestedLength)
        {
            if (!transfer.IsByteCountExact ||
                transfer.BytesTransferred < 0 ||
                transfer.BytesTransferred > requestedLength)
            {
                return false;
            }

            int confirmed = checked((int)transfer.BytesTransferred);
            output.Offset += confirmed;
            output.InvocationBytesWritten += confirmed;
            output.SeekCompleted = false;
            return true;
        }

        private WriterResult? SeekSync(long position)
        {
            DataTransferResult transfer;
            try
            {
                transfer = _syncSink!.Seek(position);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return IoFailure(exception);
            }

            return HandleControlTransfer(transfer, "seek");
        }

        private async ValueTask<WriterResult?> SeekAsync(
            long position,
            CancellationToken cancellationToken)
        {
            DataTransferResult transfer;
            try
            {
                transfer = await _asyncSink!.SeekAsync(position, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return IoFailure(exception);
            }

            return HandleControlTransfer(transfer, "seek");
        }

        private WriterResult? FlushSync()
        {
            DataTransferResult transfer;
            try
            {
                transfer = _syncSink!.Flush();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return IoFailure(exception);
            }

            return HandleControlTransfer(transfer, "flush");
        }

        private async ValueTask<WriterResult?> FlushAsync(CancellationToken cancellationToken)
        {
            DataTransferResult transfer;
            try
            {
                transfer = await _asyncSink!.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return IoFailure(exception);
            }

            return HandleControlTransfer(transfer, "flush");
        }

        private WriterResult? HandleControlTransfer(DataTransferResult transfer, string operation)
        {
            if (!transfer.IsByteCountExact || transfer.BytesTransferred != 0)
            {
                return IoFailure(new IOException(
                    $"A sink {operation} operation reported an invalid byte count."));
            }

            switch (transfer.Status)
            {
                case DataTransferStatus.Success:
                    return null;
                case DataTransferStatus.WouldBlock:
                    return new WriterResult(ExrResult.WouldBlock, transfer.PendingRange, null);
                case DataTransferStatus.Canceled:
                    throw transfer.Error as OperationCanceledException ?? new OperationCanceledException();
                case DataTransferStatus.Disposed:
                case DataTransferStatus.IoError:
                    return IoFailure(transfer.Error ?? new IOException($"The sink {operation} failed."));
                default:
                    return IoFailure(new IOException(
                        $"The sink returned an invalid status for {operation}."));
            }
        }

        private WriterResult FaultedIo(Exception error)
        {
            WriterResult result = IoFailure(error);
            lock (_stateGate)
            {
                _terminalResult = result;
                _pending = null;
                _state = WriterState.Faulted;
            }

            return result;
        }

        private WriterResult Publish(WriterResult result)
        {
            lock (_stateGate)
            {
                _pending = result.Status == ExrResult.WouldBlock ? result.Pending : null;
            }

            return result;
        }

        private void SetState(WriterState state)
        {
            lock (_stateGate)
            {
                if (!_disposed && _state != WriterState.Faulted)
                {
                    _state = state;
                    _pending = null;
                }
            }
        }

        private void SetDisposed()
        {
            lock (_stateGate)
            {
                _disposed = true;
                _pending = null;
                _state = WriterState.Disposed;
            }
        }

        private void ThrowIfDisposed()
        {
            lock (_stateGate)
            {
                ThrowIfDisposedLocked();
            }
        }

        private void ThrowIfDisposedLocked()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ExrWriter));
            }
        }

        private static void ValidatePartIndex(int partIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }
        }

        private static void ValidateTileArguments(
            int partIndex,
            int tileX,
            int tileY,
            int levelX,
            int levelY)
        {
            ValidatePartIndex(partIndex);
            if (tileX < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileX));
            }

            if (tileY < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tileY));
            }

            if (levelX < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelX));
            }

            if (levelY < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(levelY));
            }
        }

        private static WriterResult Success(long bytesWritten = 0)
        {
            return new WriterResult(ExrResult.Success, null, null, bytesWritten);
        }

        private static WriterResult Invalid(string message, Exception? error = null)
        {
            return new WriterResult(
                ExrResult.InvalidArgument,
                null,
                error ?? new ArgumentException(message));
        }

        private static WriterResult Unsupported(string message)
        {
            return new WriterResult(
                ExrResult.Unsupported,
                null,
                new NotSupportedException(message));
        }

        private static WriterResult IoFailure(Exception error)
        {
            return new WriterResult(ExrResult.IO, null, error);
        }

        private static WriterResult WithBytes(WriterResult result, long bytesWritten)
        {
            return new WriterResult(
                result.Status,
                result.Pending,
                result.Error,
                bytesWritten);
        }

        private sealed class BeginOperation
        {
            public BeginOperation(WriterFilePlan plan)
            {
                Plan = plan;
                Output = new PendingOutput(0, plan.Prefix);
            }

            public WriterFilePlan Plan { get; }

            public PendingOutput Output { get; }
        }

        private sealed class PendingBlock
        {
            public PendingBlock(
                int partIndex,
                int blockIndex,
                long startPosition,
                byte[] data,
                int maximumSamplesPerPixel = 0)
            {
                PartIndex = partIndex;
                BlockIndex = blockIndex;
                StartPosition = startPosition;
                MaximumSamplesPerPixel = maximumSamplesPerPixel;
                Output = new PendingOutput(startPosition, data);
            }

            public int PartIndex { get; }

            public int BlockIndex { get; }

            public long StartPosition { get; }

            public int MaximumSamplesPerPixel { get; }

            public PendingOutput Output { get; }
        }

        private sealed class EndOperation
        {
            public EndOperation(PendingOutput[] patches)
            {
                Patches = patches;
            }

            public PendingOutput[] Patches { get; }

            public int PatchIndex { get; set; }

            public bool EndPositionRestored { get; set; }
        }

        private sealed class PendingOutput
        {
            public PendingOutput(long absolutePosition, byte[] data)
            {
                AbsolutePosition = absolutePosition;
                Data = data;
            }

            public long AbsolutePosition { get; }

            public byte[] Data { get; }

            public int Offset { get; set; }

            public bool SeekCompleted { get; set; }

            public long InvocationBytesWritten { get; set; }
        }
    }
}
