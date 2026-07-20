using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TinyEXR.V3.IO;

namespace TinyEXR.V3
{
    /// <summary>
    /// Incremental OpenEXR metadata reader over memory or exact random-access sources.
    /// Open operations perform no I/O; ParseHeader must be called explicitly.
    /// </summary>
    public sealed class ExrReader : IDisposable, IAsyncDisposable
    {
        private readonly IExactDataSource? _syncSource;
        private readonly IAsyncExactDataSource? _asyncSource;
        private readonly IDataSourceLength _lengthSource;
        private readonly object _sourceOwner;
        private readonly bool _leaveOpen;
        private readonly ReaderLimits _limits;
        private readonly ReaderParser _parser;
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly object _stateGate = new object();

        private ReaderState _state = ReaderState.Created;
        private DataRange? _pending;
        private ReaderFileData? _fileData;
        private ReaderResult? _terminalResult;
        private ChunkOffsetReconstructionOperation? _offsetReconstruction;
        private FlatBlockOperation? _blockOperation;
        private DeepBlockOperation? _deepBlockOperation;
        private FlatReadOperation? _flatReadOperation;
        private DeepReadOperation? _deepReadOperation;
        private bool _disposed;

        private ExrReader(
            IExactDataSource? syncSource,
            IAsyncExactDataSource? asyncSource,
            IDataSourceLength lengthSource,
            object sourceOwner,
            ReaderOptions options)
        {
            _syncSource = syncSource;
            _asyncSource = asyncSource;
            _lengthSource = lengthSource;
            _sourceOwner = sourceOwner;
            _leaveOpen = options.LeaveOpen;
            _limits = options.Limits;
            _parser = new ReaderParser(options.Limits);
        }

        public ReaderState State
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
        /// First missing range from the most recent WouldBlock result. Prefer the operation result
        /// itself when coordinating multiple callers.
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

        public int NumParts => GetReadyData().Parts.Length;

        public static ExrReader OpenMemory(
            ReadOnlyMemory<byte> data,
            ReaderOptions? options = null)
        {
            ReaderOptions effectiveOptions = options ?? new ReaderOptions();
            MemoryDataSource source = new MemoryDataSource(data);
            return new ExrReader(source, source, source, source, effectiveOptions);
        }

        public static ExrReader OpenSource(
            IExactDataSource source,
            ReaderOptions? options = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            ReaderOptions effectiveOptions = options ?? new ReaderOptions();
            return new ExrReader(
                source,
                source as IAsyncExactDataSource,
                source,
                source,
                effectiveOptions);
        }

        public static ExrReader OpenAsyncSource(
            IAsyncExactDataSource source,
            ReaderOptions? options = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            ReaderOptions effectiveOptions = options ?? new ReaderOptions();
            return new ExrReader(
                source as IExactDataSource,
                source,
                source,
                source,
                effectiveOptions);
        }

        public ReaderResult ParseHeader()
        {
            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with an asynchronous-only source.");
                }

                ReaderResult? existing = GetExistingResult();
                if (existing.HasValue)
                {
                    return Publish(existing.Value);
                }

                ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
                if (lengthFailure.HasValue)
                {
                    return Publish(lengthFailure.Value);
                }

                SetParserState();
                for (;;)
                {
                    ReaderParserRequest request = _parser.GetNextRequest();
                    DataTransferResult transfer;
                    try
                    {
                        transfer = _syncSource.ReadExactly(request.Offset, request.Span);
                    }
                    catch (ObjectDisposedException exception)
                    {
                        return Publish(IoFailure(exception));
                    }
                    catch (IOException exception)
                    {
                        return Publish(IoFailure(exception));
                    }

                    ReaderResult? transferResult = HandleTransferResult(transfer, request);
                    if (transferResult.HasValue)
                    {
                        return Publish(transferResult.Value);
                    }

                    ReaderResult? parserResult = AcceptParserRequest(knownLength);
                    if (parserResult.HasValue)
                    {
                        return Publish(parserResult.Value);
                    }

                    if (_parser.IsComplete)
                    {
                        return PublishReady();
                    }
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async ValueTask<ReaderResult> ParseHeaderAsync(
            CancellationToken cancellationToken = default)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with a synchronous-only source.");
                }

                ReaderResult? existing = GetExistingResult();
                if (existing.HasValue)
                {
                    return Publish(existing.Value);
                }

                ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
                if (lengthFailure.HasValue)
                {
                    return Publish(lengthFailure.Value);
                }

                SetParserState();
                for (;;)
                {
                    ReaderParserRequest request = _parser.GetNextRequest();
                    DataTransferResult transfer;
                    try
                    {
                        transfer = await _asyncSource.ReadExactlyAsync(
                            request.Offset,
                            request.Memory,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (ObjectDisposedException exception)
                    {
                        return Publish(IoFailure(exception));
                    }
                    catch (IOException exception)
                    {
                        return Publish(IoFailure(exception));
                    }

                    ReaderResult? transferResult = HandleTransferResult(transfer, request);
                    if (transferResult.HasValue)
                    {
                        return Publish(transferResult.Value);
                    }

                    ReaderResult? parserResult = AcceptParserRequest(knownLength);
                    if (parserResult.HasValue)
                    {
                        return Publish(parserResult.Value);
                    }

                    if (_parser.IsComplete)
                    {
                        return PublishReady();
                    }
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

            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            return data.Parts[partIndex].Header;
        }

        internal uint GetRawVersionField()
        {
            return GetReadyData().RawVersionField;
        }

        internal long GetHeaderEndOffset(int partIndex)
        {
            return GetPartData(partIndex).HeaderEnd;
        }

        internal bool HasHeaderAttribute(int partIndex, string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            ReaderPartData part = GetPartData(partIndex);
            return name switch
            {
                "name" => part.HasNameAttribute,
                "type" => part.HasTypeAttribute,
                _ => false,
            };
        }

        internal IReadOnlyList<HeaderAttribute> GetRawHeaderAttributes(int partIndex)
        {
            return GetPartData(partIndex).RawAttributes;
        }

        public int GetNumBlocks(int partIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            return data.Parts[partIndex].Offsets.Length;
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

            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            ReaderPartData part = data.Parts[partIndex];
            if (blockIndex >= part.Offsets.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            return part.GetBlockInfo(blockIndex);
        }

        /// <summary>
        /// Decodes one flat scanline block or tile into canonical EXR bytes. The destination is
        /// modified only after the full chunk has been fetched, validated, and decoded.
        /// </summary>
        public ReaderResult DecodeBlock(
            int partIndex,
            int blockIndex,
            Span<byte> destination)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with an asynchronous-only source.");
                }

                return Publish(DecodeBlockCore(partIndex, blockIndex, destination));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Asynchronously decodes one flat scanline block or tile into canonical EXR bytes.
        /// </summary>
        public async ValueTask<ReaderResult> DecodeBlockAsync(
            int partIndex,
            int blockIndex,
            Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with a synchronous-only source.");
                }

                ReaderResult result = await DecodeBlockCoreAsync(
                    partIndex,
                    blockIndex,
                    destination,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Decodes the per-pixel sample counts for one deep block. Counts are published only
        /// after the complete table has been fetched, decompressed, and validated.
        /// </summary>
        public ReaderResult DecodeDeepCounts(
            int partIndex,
            int blockIndex,
            Span<int> counts)
        {
            ValidateBlockArguments(partIndex, blockIndex);
            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with an asynchronous-only source.");
                }

                AbandonDeepReadOperation();
                return Publish(DecodeDeepCountsCore(partIndex, blockIndex, counts));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>Asynchronously decodes the per-pixel sample counts for one deep block.</summary>
        public async ValueTask<ReaderResult> DecodeDeepCountsAsync(
            int partIndex,
            int blockIndex,
            Memory<int> counts,
            CancellationToken cancellationToken = default)
        {
            ValidateBlockArguments(partIndex, blockIndex);
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with a synchronous-only source.");
                }

                AbandonDeepReadOperation();
                ReaderResult result = await DecodeDeepCountsCoreAsync(
                    partIndex,
                    blockIndex,
                    counts,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// Decodes one deep block into channel-planar sample buffers. DecodeDeepCounts must
        /// first succeed for the same block and the supplied counts must match that result.
        /// </summary>
        public ReaderResult DecodeDeepSamples(
            int partIndex,
            int blockIndex,
            ReadOnlySpan<int> counts,
            IReadOnlyList<DeepChannelDestination> destinations)
        {
            ValidateBlockArguments(partIndex, blockIndex);
            if (destinations == null)
            {
                throw new ArgumentNullException(nameof(destinations));
            }

            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with an asynchronous-only source.");
                }

                AbandonDeepReadOperation();
                return Publish(DecodeDeepSamplesCore(
                    partIndex,
                    blockIndex,
                    counts,
                    destinations));
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>Asynchronously decodes one deep block into channel-planar sample buffers.</summary>
        public async ValueTask<ReaderResult> DecodeDeepSamplesAsync(
            int partIndex,
            int blockIndex,
            ReadOnlyMemory<int> counts,
            IReadOnlyList<DeepChannelDestination> destinations,
            CancellationToken cancellationToken = default)
        {
            ValidateBlockArguments(partIndex, blockIndex);
            if (destinations == null)
            {
                throw new ArgumentNullException(nameof(destinations));
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with a synchronous-only source.");
                }

                AbandonDeepReadOperation();
                ReaderResult result = await DecodeDeepSamplesCoreAsync(
                    partIndex,
                    blockIndex,
                    counts,
                    destinations,
                    cancellationToken).ConfigureAwait(false);
                return Publish(result);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>Materializes every level of one flat or deep part into planar channel buffers.</summary>
        public ReaderResult<Part> ReadPart(int partIndex)
        {
            ValidatePartIndex(partIndex);
            return ReadFlat(FlatReadRequest.Part(partIndex));
        }

        /// <summary>Asynchronously materializes every level of one flat or deep part.</summary>
        public ValueTask<ReaderResult<Part>> ReadPartAsync(
            int partIndex,
            CancellationToken cancellationToken = default)
        {
            ValidatePartIndex(partIndex);
            return ReadFlatAsync(FlatReadRequest.Part(partIndex), cancellationToken);
        }

        /// <summary>Materializes an inclusive range of a flat or deep scanline part.</summary>
        public ReaderResult<Part> ReadScanlines(int partIndex, int minimumY, int lineCount)
        {
            ValidatePartIndex(partIndex);
            if (lineCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineCount));
            }

            return ReadFlat(FlatReadRequest.Scanlines(partIndex, minimumY, lineCount));
        }

        /// <summary>Asynchronously materializes an inclusive range of a flat or deep scanline part.</summary>
        public ValueTask<ReaderResult<Part>> ReadScanlinesAsync(
            int partIndex,
            int minimumY,
            int lineCount,
            CancellationToken cancellationToken = default)
        {
            ValidatePartIndex(partIndex);
            if (lineCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(lineCount));
            }

            return ReadFlatAsync(
                FlatReadRequest.Scanlines(partIndex, minimumY, lineCount),
                cancellationToken);
        }

        /// <summary>Materializes one tile at one level of a flat or deep tiled part.</summary>
        public ReaderResult<Part> ReadTile(
            int partIndex,
            int tileX,
            int tileY,
            int levelX = 0,
            int levelY = 0)
        {
            ValidateTileArguments(partIndex, tileX, tileY, levelX, levelY);
            return ReadFlat(FlatReadRequest.Tile(partIndex, tileX, tileY, levelX, levelY));
        }

        /// <summary>Asynchronously materializes one tile at one level of a flat or deep tiled part.</summary>
        public ValueTask<ReaderResult<Part>> ReadTileAsync(
            int partIndex,
            int tileX,
            int tileY,
            int levelX = 0,
            int levelY = 0,
            CancellationToken cancellationToken = default)
        {
            ValidateTileArguments(partIndex, tileX, tileY, levelX, levelY);
            return ReadFlatAsync(
                FlatReadRequest.Tile(partIndex, tileX, tileY, levelX, levelY),
                cancellationToken);
        }

        public void Dispose()
        {
            _operationGate.Wait();
            try
            {
                if (!MarkDisposed())
                {
                    return;
                }

                if (!_leaveOpen)
                {
                    if (_sourceOwner is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                    else if (_sourceOwner is IAsyncDisposable asyncDisposable)
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
                if (!MarkDisposed())
                {
                    return;
                }

                if (!_leaveOpen)
                {
                    if (_sourceOwner is IAsyncDisposable asyncDisposable)
                    {
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    }
                    else if (_sourceOwner is IDisposable disposable)
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

        private ReaderResult? GetExistingResult()
        {
            lock (_stateGate)
            {
                if (_state == ReaderState.Ready)
                {
                    return Success();
                }

                if (_terminalResult.HasValue)
                {
                    return _terminalResult.Value;
                }

                return null;
            }
        }

        private ReaderResult? TryGetKnownLength(out long? knownLength)
        {
            knownLength = null;
            try
            {
                if (_lengthSource.TryGetLength(out long length))
                {
                    if (length < 0)
                    {
                        return IoFailure(new InvalidDataException("The data source reported a negative length."));
                    }

                    knownLength = length;
                }

                return null;
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is ObjectDisposedException ||
                exception is InvalidOperationException)
            {
                return IoFailure(exception);
            }
        }

        private ReaderResult? HandleTransferResult(
            DataTransferResult transfer,
            ReaderParserRequest request)
        {
            switch (transfer.Status)
            {
                case DataTransferStatus.Success:
                    if (!transfer.IsByteCountExact || transfer.BytesTransferred != request.Length)
                    {
                        return IoFailure(new IOException(
                            "An exact data source reported success without the requested exact byte count."));
                    }

                    return null;
                case DataTransferStatus.WouldBlock:
                    if (!transfer.PendingRange.HasValue ||
                        transfer.PendingRange.Value.Length <= 0 ||
                        transfer.PendingRange.Value.Offset < request.Offset ||
                        transfer.PendingRange.Value.End > checked(request.Offset + request.Length))
                    {
                        return IoFailure(new IOException(
                            "An exact data source returned an invalid pending range."));
                    }

                    return new ReaderResult(ExrResult.WouldBlock, transfer.PendingRange, null);
                case DataTransferStatus.EndOfSource:
                    ExrResult eofResult = _parser.State == ReaderState.ReadingPrefix
                        ? ExrResult.InvalidFile
                        : ExrResult.Corrupt;
                    return Terminal(new ReaderResult(
                        eofResult,
                        null,
                        new EndOfStreamException("The source ended before EXR metadata parsing completed.")));
                case DataTransferStatus.Canceled:
                    throw transfer.Error as OperationCanceledException ?? new OperationCanceledException();
                case DataTransferStatus.Disposed:
                case DataTransferStatus.IoError:
                    return IoFailure(transfer.Error ?? new IOException("The exact data source failed."));
                default:
                    return IoFailure(new IOException("The exact data source returned an unknown status."));
            }
        }

        private ReaderResult? AcceptParserRequest(long? knownLength)
        {
            try
            {
                _parser.AcceptNextRequest(knownLength);
                SetParserState();
                return null;
            }
            catch (ReaderParseException exception)
            {
                Exception error = exception.InnerException ?? exception;
                return Terminal(new ReaderResult(exception.Result, null, error));
            }
            catch (OutOfMemoryException exception)
            {
                return Terminal(new ReaderResult(ExrResult.OutOfMemory, null, exception));
            }
            catch (OverflowException exception)
            {
                return Terminal(new ReaderResult(ExrResult.Corrupt, null, exception));
            }
        }

        private ReaderResult Terminal(ReaderResult result)
        {
            lock (_stateGate)
            {
                _terminalResult = result;
                _pending = null;
                if (result.Status == ExrResult.Corrupt)
                {
                    _state = ReaderState.Faulted;
                }
            }

            return result;
        }

        private ReaderResult PublishReady()
        {
            ReaderFileData data = _parser.FileData ??
                throw new InvalidOperationException("The completed parser did not publish reader data.");
            lock (_stateGate)
            {
                _fileData = data;
                _pending = null;
                _state = ReaderState.Ready;
            }

            return Success();
        }

        private ReaderResult Publish(ReaderResult result)
        {
            lock (_stateGate)
            {
                _pending = result.Status == ExrResult.WouldBlock ? result.Pending : null;
            }

            return result;
        }

        private void SetParserState()
        {
            ReaderState parserState = _parser.State;
            if (parserState == ReaderState.Ready)
            {
                // PublishReady commits the immutable metadata and Ready state atomically.
                return;
            }

            lock (_stateGate)
            {
                if (!_disposed && _state != ReaderState.Faulted)
                {
                    _state = parserState;
                }
            }
        }

        private ReaderFileData GetReadyData()
        {
            lock (_stateGate)
            {
                ThrowIfDisposedLocked();
                if (_state != ReaderState.Ready || _fileData == null)
                {
                    throw new InvalidOperationException("ParseHeader must succeed before metadata is accessed.");
                }

                return _fileData;
            }
        }

        private ReaderPartData GetPartData(int partIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            return data.Parts[partIndex];
        }

        private bool MarkDisposed()
        {
            lock (_stateGate)
            {
                if (_disposed)
                {
                    return false;
                }

                _disposed = true;
                _offsetReconstruction = null;
                _blockOperation = null;
                _deepBlockOperation = null;
                _flatReadOperation = null;
                _deepReadOperation = null;
                _pending = null;
                _state = ReaderState.Disposed;
                return true;
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
                throw new ObjectDisposedException(nameof(ExrReader));
            }
        }

        private static ReaderResult Success()
        {
            return new ReaderResult(ExrResult.Success, null, null);
        }

        private static ReaderResult IoFailure(Exception error)
        {
            return new ReaderResult(ExrResult.IO, null, error);
        }

        private bool IsDeepPart(int partIndex)
        {
            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            return data.Parts[partIndex].Header.IsDeep;
        }

        private void AbandonDeepReadOperation()
        {
            if (_deepReadOperation == null)
            {
                return;
            }

            _deepReadOperation = null;
            _deepBlockOperation = null;
        }

        private ReaderResult<Part> ReadFlat(FlatReadRequest request)
        {
            _operationGate.Wait();
            try
            {
                ThrowIfDisposed();
                if (_syncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with an asynchronous-only source.");
                }

                if (IsDeepPart(request.PartIndex))
                {
                    return ReadDeepCore(request);
                }

                AbandonDeepReadOperation();
                return ReadFlatCore(request);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private async ValueTask<ReaderResult<Part>> ReadFlatAsync(
            FlatReadRequest request,
            CancellationToken cancellationToken)
        {
            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (_asyncSource == null)
                {
                    throw new NotSupportedException("This reader was opened with a synchronous-only source.");
                }

                if (IsDeepPart(request.PartIndex))
                {
                    return await ReadDeepCoreAsync(request, cancellationToken).ConfigureAwait(false);
                }

                AbandonDeepReadOperation();
                return await ReadFlatCoreAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private ReaderResult<Part> ReadFlatCore(FlatReadRequest request)
        {
            ReaderResult? setup = PrepareFlatReadOperation(request, out FlatReadOperation? operation);
            if (setup.HasValue)
            {
                return FinishFlatRead(setup.Value, value: null);
            }

            for (;;)
            {
                Memory<byte> canonical;
                try
                {
                    canonical = operation!.GetCanonicalDestination();
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishFlatRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (NotSupportedException exception)
                {
                    return FinishFlatRead(
                        new ReaderResult(ExrResult.Unsupported, null, exception),
                        value: null);
                }

                ReaderResult blockResult = DecodeBlockCore(
                    request.PartIndex,
                    operation.CurrentBlockIndex,
                    canonical.Span);
                if (!blockResult.IsSuccess)
                {
                    return FinishFlatRead(blockResult, value: null);
                }

                ReaderResult? scatter = operation.AcceptDecodedBlock();
                if (scatter.HasValue)
                {
                    return FinishFlatRead(scatter.Value, value: null);
                }

                if (operation.IsComplete)
                {
                    return PublishCompletedFlatRead(operation);
                }
            }
        }

        private async ValueTask<ReaderResult<Part>> ReadFlatCoreAsync(
            FlatReadRequest request,
            CancellationToken cancellationToken)
        {
            ReaderResult? setup = PrepareFlatReadOperation(request, out FlatReadOperation? operation);
            if (setup.HasValue)
            {
                return FinishFlatRead(setup.Value, value: null);
            }

            for (;;)
            {
                Memory<byte> canonical;
                try
                {
                    canonical = operation!.GetCanonicalDestination();
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishFlatRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (NotSupportedException exception)
                {
                    return FinishFlatRead(
                        new ReaderResult(ExrResult.Unsupported, null, exception),
                        value: null);
                }

                ReaderResult blockResult = await DecodeBlockCoreAsync(
                    request.PartIndex,
                    operation.CurrentBlockIndex,
                    canonical,
                    cancellationToken).ConfigureAwait(false);
                if (!blockResult.IsSuccess)
                {
                    return FinishFlatRead(blockResult, value: null);
                }

                ReaderResult? scatter = operation.AcceptDecodedBlock();
                if (scatter.HasValue)
                {
                    return FinishFlatRead(scatter.Value, value: null);
                }

                if (operation.IsComplete)
                {
                    return PublishCompletedFlatRead(operation);
                }
            }
        }

        private ReaderResult<Part> ReadDeepCore(FlatReadRequest request)
        {
            ReaderResult? setup = PrepareDeepReadOperation(request, out DeepReadOperation? operation);
            if (setup.HasValue)
            {
                return FinishDeepRead(setup.Value, value: null);
            }

            while (!operation!.CountsComplete)
            {
                Memory<int> countDestination;
                try
                {
                    countDestination = operation.GetCountDestination();
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (Exception exception) when (
                    exception is NotSupportedException || exception is OverflowException)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.Unsupported, null, exception),
                        value: null);
                }

                ReaderResult countResult = DecodeDeepCountsCore(
                    request.PartIndex,
                    operation.CurrentCountBlockIndex,
                    countDestination.Span);
                if (!countResult.IsSuccess)
                {
                    return FinishDeepRead(countResult, value: null);
                }

                DeepBlockOperation completed = _deepBlockOperation ??
                    throw new InvalidOperationException("The deep count decoder did not retain its completed block state.");
                _deepBlockOperation = null;
                ReaderResult? accepted;
                try
                {
                    accepted = operation.AcceptDecodedCounts(completed);
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is InvalidOperationException ||
                    exception is OverflowException)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.Corrupt, null, exception),
                        value: null);
                }

                if (accepted.HasValue)
                {
                    return FinishDeepRead(accepted.Value, value: null);
                }
            }

            while (!operation.IsComplete)
            {
                DeepBlockOperation blockOperation;
                ReadOnlyMemory<int> counts;
                IReadOnlyList<DeepChannelDestination> destinations;
                try
                {
                    operation.GetSampleArguments(
                        out blockOperation,
                        out counts,
                        out destinations);
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is InvalidOperationException ||
                    exception is OverflowException)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.Corrupt, null, exception),
                        value: null);
                }

                if (_deepBlockOperation == null)
                {
                    _deepBlockOperation = blockOperation;
                }
                else if (!ReferenceEquals(_deepBlockOperation, blockOperation))
                {
                    return FinishDeepRead(
                        new ReaderResult(
                            ExrResult.IO,
                            null,
                            new InvalidOperationException("A different deep block operation is active.")),
                        value: null);
                }

                ReaderResult sampleResult = DecodeDeepSamplesCore(
                    request.PartIndex,
                    operation.CurrentSampleBlockIndex,
                    counts.Span,
                    destinations);
                if (!sampleResult.IsSuccess)
                {
                    return FinishDeepRead(sampleResult, value: null);
                }

                ReaderResult? accepted = operation.AcceptDecodedSamples();
                if (accepted.HasValue)
                {
                    return FinishDeepRead(accepted.Value, value: null);
                }
            }

            return PublishCompletedDeepRead(operation);
        }

        private async ValueTask<ReaderResult<Part>> ReadDeepCoreAsync(
            FlatReadRequest request,
            CancellationToken cancellationToken)
        {
            ReaderResult? setup = PrepareDeepReadOperation(request, out DeepReadOperation? operation);
            if (setup.HasValue)
            {
                return FinishDeepRead(setup.Value, value: null);
            }

            while (!operation!.CountsComplete)
            {
                Memory<int> countDestination;
                try
                {
                    countDestination = operation.GetCountDestination();
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (Exception exception) when (
                    exception is NotSupportedException || exception is OverflowException)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.Unsupported, null, exception),
                        value: null);
                }

                ReaderResult countResult = await DecodeDeepCountsCoreAsync(
                    request.PartIndex,
                    operation.CurrentCountBlockIndex,
                    countDestination,
                    cancellationToken).ConfigureAwait(false);
                if (!countResult.IsSuccess)
                {
                    return FinishDeepRead(countResult, value: null);
                }

                DeepBlockOperation completed = _deepBlockOperation ??
                    throw new InvalidOperationException("The deep count decoder did not retain its completed block state.");
                _deepBlockOperation = null;
                ReaderResult? accepted;
                try
                {
                    accepted = operation.AcceptDecodedCounts(completed);
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is InvalidOperationException ||
                    exception is OverflowException)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.Corrupt, null, exception),
                        value: null);
                }

                if (accepted.HasValue)
                {
                    return FinishDeepRead(accepted.Value, value: null);
                }
            }

            while (!operation.IsComplete)
            {
                DeepBlockOperation blockOperation;
                ReadOnlyMemory<int> counts;
                IReadOnlyList<DeepChannelDestination> destinations;
                try
                {
                    operation.GetSampleArguments(
                        out blockOperation,
                        out counts,
                        out destinations);
                }
                catch (OutOfMemoryException exception)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.OutOfMemory, null, exception),
                        value: null);
                }
                catch (Exception exception) when (
                    exception is ArgumentException ||
                    exception is InvalidOperationException ||
                    exception is OverflowException)
                {
                    return FinishDeepRead(
                        new ReaderResult(ExrResult.Corrupt, null, exception),
                        value: null);
                }

                if (_deepBlockOperation == null)
                {
                    _deepBlockOperation = blockOperation;
                }
                else if (!ReferenceEquals(_deepBlockOperation, blockOperation))
                {
                    return FinishDeepRead(
                        new ReaderResult(
                            ExrResult.IO,
                            null,
                            new InvalidOperationException("A different deep block operation is active.")),
                        value: null);
                }

                ReaderResult sampleResult = await DecodeDeepSamplesCoreAsync(
                    request.PartIndex,
                    operation.CurrentSampleBlockIndex,
                    counts,
                    destinations,
                    cancellationToken).ConfigureAwait(false);
                if (!sampleResult.IsSuccess)
                {
                    return FinishDeepRead(sampleResult, value: null);
                }

                ReaderResult? accepted = operation.AcceptDecodedSamples();
                if (accepted.HasValue)
                {
                    return FinishDeepRead(accepted.Value, value: null);
                }
            }

            return PublishCompletedDeepRead(operation);
        }

        private ReaderResult? PrepareDeepReadOperation(
            FlatReadRequest request,
            out DeepReadOperation? operation)
        {
            ReaderFileData data = GetReadyData();
            if (request.PartIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(request.PartIndex));
            }

            if (_deepReadOperation != null && _deepReadOperation.Request.Equals(request))
            {
                operation = _deepReadOperation;
                return null;
            }

            if (_deepReadOperation != null)
            {
                _deepBlockOperation = null;
            }

            _deepReadOperation = null;
            if (_flatReadOperation != null)
            {
                _flatReadOperation = null;
                _blockOperation = null;
            }

            try
            {
                operation = DeepReadOperation.Create(
                    request,
                    data.Parts[request.PartIndex],
                    _limits);
                _deepReadOperation = operation;
                _deepBlockOperation = null;
                ClearPending();
                return null;
            }
            catch (ReaderLimitExceededException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.Unsupported, null, exception);
            }
            catch (NotSupportedException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.Unsupported, null, exception);
            }
            catch (OutOfMemoryException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.Unsupported, null, exception);
            }
        }

        private ReaderResult<Part> PublishCompletedDeepRead(DeepReadOperation operation)
        {
            try
            {
                Part part = operation.BuildPart();
                ReaderResult result = new ReaderResult(
                    ExrResult.Success,
                    null,
                    null,
                    operation.MaterializedByteCount);
                _deepReadOperation = null;
                _deepBlockOperation = null;
                return new ReaderResult<Part>(Publish(result), part);
            }
            catch (OutOfMemoryException exception)
            {
                return FinishDeepRead(
                    new ReaderResult(ExrResult.OutOfMemory, null, exception),
                    value: null);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is OverflowException)
            {
                return FinishDeepRead(
                    new ReaderResult(ExrResult.Corrupt, null, exception),
                    value: null);
            }
        }

        private ReaderResult<Part> FinishDeepRead(ReaderResult result, Part? value)
        {
            if (result.Status != ExrResult.WouldBlock && result.Status != ExrResult.IO)
            {
                _deepReadOperation = null;
                _deepBlockOperation = null;
            }

            return new ReaderResult<Part>(Publish(result), value);
        }

        private ReaderResult? PrepareFlatReadOperation(
            FlatReadRequest request,
            out FlatReadOperation? operation)
        {
            ReaderFileData data = GetReadyData();
            if (request.PartIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(request.PartIndex));
            }

            if (_flatReadOperation != null && _flatReadOperation.Request.Equals(request))
            {
                operation = _flatReadOperation;
                return null;
            }

            _flatReadOperation = null;
            try
            {
                operation = FlatReadOperation.Create(
                    request,
                    data.Parts[request.PartIndex],
                    _limits);
                _flatReadOperation = operation;
                ClearPending();
                return null;
            }
            catch (ReaderLimitExceededException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.Unsupported, null, exception);
            }
            catch (NotSupportedException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.Unsupported, null, exception);
            }
            catch (OutOfMemoryException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                operation = null;
                return new ReaderResult(ExrResult.Unsupported, null, exception);
            }
        }

        private ReaderResult<Part> PublishCompletedFlatRead(FlatReadOperation operation)
        {
            try
            {
                Part part = operation.BuildPart();
                ReaderResult result = new ReaderResult(
                    ExrResult.Success,
                    null,
                    null,
                    operation.MaterializedByteCount);
                _flatReadOperation = null;
                return new ReaderResult<Part>(Publish(result), part);
            }
            catch (OutOfMemoryException exception)
            {
                return FinishFlatRead(
                    new ReaderResult(ExrResult.OutOfMemory, null, exception),
                    value: null);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is NotSupportedException ||
                exception is OverflowException)
            {
                return FinishFlatRead(
                    new ReaderResult(ExrResult.Corrupt, null, exception),
                    value: null);
            }
        }

        private ReaderResult<Part> FinishFlatRead(ReaderResult result, Part? value)
        {
            if (result.Status != ExrResult.WouldBlock && result.Status != ExrResult.IO)
            {
                _flatReadOperation = null;
            }

            return new ReaderResult<Part>(Publish(result), value);
        }

        private static void ValidatePartIndex(int partIndex)
        {
            if (partIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }
        }

        private static void ValidateBlockArguments(int partIndex, int blockIndex)
        {
            ValidatePartIndex(partIndex);
            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
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

        private ReaderResult DecodeDeepCountsCore(
            int partIndex,
            int blockIndex,
            Span<int> counts)
        {
            ReaderResult? setup = PrepareDeepCountsOperation(
                partIndex,
                blockIndex,
                counts.Length,
                out DeepBlockOperation? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
            if (lengthFailure.HasValue)
            {
                return lengthFailure.Value;
            }

            for (;;)
            {
                ReaderParserRequest request;
                bool reconstructing = operation == null;
                if (reconstructing)
                {
                    ReaderResult? reconstruction = AdvanceOffsetReconstruction(knownLength);
                    if (reconstruction.HasValue)
                    {
                        return reconstruction.Value;
                    }

                    if (_offsetReconstruction == null)
                    {
                        setup = PrepareDeepCountsOperation(
                            partIndex,
                            blockIndex,
                            counts.Length,
                            out operation);
                        if (setup.HasValue)
                        {
                            return setup.Value;
                        }

                        continue;
                    }

                    request = _offsetReconstruction.GetNextRequest();
                }
                else
                {
                    ReaderResult? completion = AdvanceDeepCountsOperation(
                        operation!,
                        knownLength,
                        counts);
                    if (completion.HasValue)
                    {
                        return completion.Value;
                    }

                    request = operation!.GetNextCountsRequest();
                }

                DataTransferResult transfer;
                try
                {
                    transfer = _syncSource!.ReadExactly(request.Offset, request.Span);
                }
                catch (ObjectDisposedException exception)
                {
                    return IoFailure(exception);
                }
                catch (IOException exception)
                {
                    return IoFailure(exception);
                }

                ReaderResult? transferResult = reconstructing
                    ? HandleOffsetReconstructionTransferResult(transfer, request)
                    : HandleDeepTransferResult(transfer, request);
                if (transferResult.HasValue)
                {
                    return transferResult.Value;
                }

                if (reconstructing)
                {
                    _offsetReconstruction!.AcceptRequest(request.Length);
                }
                else
                {
                    operation!.AcceptCountsRequest(request.Length);
                }
            }
        }

        private async ValueTask<ReaderResult> DecodeDeepCountsCoreAsync(
            int partIndex,
            int blockIndex,
            Memory<int> counts,
            CancellationToken cancellationToken)
        {
            ReaderResult? setup = PrepareDeepCountsOperation(
                partIndex,
                blockIndex,
                counts.Length,
                out DeepBlockOperation? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
            if (lengthFailure.HasValue)
            {
                return lengthFailure.Value;
            }

            for (;;)
            {
                ReaderParserRequest request;
                bool reconstructing = operation == null;
                if (reconstructing)
                {
                    ReaderResult? reconstruction = AdvanceOffsetReconstruction(knownLength);
                    if (reconstruction.HasValue)
                    {
                        return reconstruction.Value;
                    }

                    if (_offsetReconstruction == null)
                    {
                        setup = PrepareDeepCountsOperation(
                            partIndex,
                            blockIndex,
                            counts.Length,
                            out operation);
                        if (setup.HasValue)
                        {
                            return setup.Value;
                        }

                        continue;
                    }

                    request = _offsetReconstruction.GetNextRequest();
                }
                else
                {
                    ReaderResult? completion = AdvanceDeepCountsOperation(
                        operation!,
                        knownLength,
                        counts.Span);
                    if (completion.HasValue)
                    {
                        return completion.Value;
                    }

                    request = operation!.GetNextCountsRequest();
                }

                DataTransferResult transfer;
                try
                {
                    transfer = await _asyncSource!.ReadExactlyAsync(
                        request.Offset,
                        request.Memory,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ObjectDisposedException exception)
                {
                    return IoFailure(exception);
                }
                catch (IOException exception)
                {
                    return IoFailure(exception);
                }

                ReaderResult? transferResult = reconstructing
                    ? HandleOffsetReconstructionTransferResult(transfer, request)
                    : HandleDeepTransferResult(transfer, request);
                if (transferResult.HasValue)
                {
                    return transferResult.Value;
                }

                if (reconstructing)
                {
                    _offsetReconstruction!.AcceptRequest(request.Length);
                }
                else
                {
                    operation!.AcceptCountsRequest(request.Length);
                }
            }
        }

        private ReaderResult DecodeDeepSamplesCore(
            int partIndex,
            int blockIndex,
            ReadOnlySpan<int> counts,
            IReadOnlyList<DeepChannelDestination> destinations)
        {
            DeepBlockOperation operation = PrepareDeepSamplesOperation(
                partIndex,
                blockIndex,
                counts,
                destinations);
            ReaderResult? preparation = PrepareDeepSamplePayload(operation);
            if (preparation.HasValue)
            {
                return preparation.Value;
            }

            for (;;)
            {
                ReaderResult? completion = AdvanceDeepSamplesOperation(operation, destinations);
                if (completion.HasValue)
                {
                    return completion.Value;
                }

                ReaderParserRequest request = operation.GetNextSamplesRequest();
                DataTransferResult transfer;
                try
                {
                    transfer = _syncSource!.ReadExactly(request.Offset, request.Span);
                }
                catch (ObjectDisposedException exception)
                {
                    return IoFailure(exception);
                }
                catch (IOException exception)
                {
                    return IoFailure(exception);
                }

                ReaderResult? transferResult = HandleDeepTransferResult(transfer, request);
                if (transferResult.HasValue)
                {
                    return transferResult.Value;
                }

                operation.AcceptSamplesRequest(request.Length);
            }
        }

        private async ValueTask<ReaderResult> DecodeDeepSamplesCoreAsync(
            int partIndex,
            int blockIndex,
            ReadOnlyMemory<int> counts,
            IReadOnlyList<DeepChannelDestination> destinations,
            CancellationToken cancellationToken)
        {
            DeepBlockOperation operation = PrepareDeepSamplesOperation(
                partIndex,
                blockIndex,
                counts.Span,
                destinations);
            ReaderResult? preparation = PrepareDeepSamplePayload(operation);
            if (preparation.HasValue)
            {
                return preparation.Value;
            }

            for (;;)
            {
                ReaderResult? completion = AdvanceDeepSamplesOperation(operation, destinations);
                if (completion.HasValue)
                {
                    return completion.Value;
                }

                ReaderParserRequest request = operation.GetNextSamplesRequest();
                DataTransferResult transfer;
                try
                {
                    transfer = await _asyncSource!.ReadExactlyAsync(
                        request.Offset,
                        request.Memory,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ObjectDisposedException exception)
                {
                    return IoFailure(exception);
                }
                catch (IOException exception)
                {
                    return IoFailure(exception);
                }

                ReaderResult? transferResult = HandleDeepTransferResult(transfer, request);
                if (transferResult.HasValue)
                {
                    return transferResult.Value;
                }

                operation.AcceptSamplesRequest(request.Length);
            }
        }

        private ReaderResult? PrepareDeepCountsOperation(
            int partIndex,
            int blockIndex,
            int destinationLength,
            out DeepBlockOperation? operation)
        {
            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            ReaderPartData part = data.Parts[partIndex];
            if (blockIndex >= part.Offsets.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            BlockInfo info = part.GetBlockInfo(blockIndex);
            if (!info.IsDeep)
            {
                operation = null;
                return new ReaderResult(
                    ExrResult.Unsupported,
                    null,
                    new NotSupportedException("Flat blocks require the flat block decode API."));
            }

            long requiredCount = checked(info.Region.Width * info.Region.Height);
            long countByteCount = checked(requiredCount * sizeof(int));
            if (requiredCount > int.MaxValue ||
                countByteCount > _limits.MaximumUncompressedBlockByteCount)
            {
                operation = null;
                return new ReaderResult(
                    ExrResult.Unsupported,
                    null,
                    new ReaderLimitExceededException(
                        nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                        countByteCount,
                        _limits.MaximumUncompressedBlockByteCount));
            }

            if (destinationLength < requiredCount)
            {
                throw new ArgumentException(
                    $"The count buffer contains {destinationLength} entries; {requiredCount} entries are required.",
                    "counts");
            }

            if (info.IsMissing)
            {
                operation = null;
                _deepBlockOperation = null;
                if (_offsetReconstruction == null)
                {
                    try
                    {
                        _offsetReconstruction = new ChunkOffsetReconstructionOperation(data, _limits);
                    }
                    catch (OutOfMemoryException exception)
                    {
                        return new ReaderResult(ExrResult.OutOfMemory, null, exception);
                    }
                    catch (OverflowException exception)
                    {
                        return new ReaderResult(ExrResult.Corrupt, null, exception);
                    }

                    ClearPending();
                }

                return null;
            }

            if (_deepBlockOperation == null ||
                _deepBlockOperation.Info.PartIndex != partIndex ||
                _deepBlockOperation.Info.BlockIndex != blockIndex)
            {
                try
                {
                    _deepBlockOperation = new DeepBlockOperation(
                        part.Header,
                        info,
                        data.Multipart,
                        _limits);
                }
                catch (OutOfMemoryException exception)
                {
                    operation = null;
                    return new ReaderResult(ExrResult.OutOfMemory, null, exception);
                }
                catch (OverflowException exception)
                {
                    operation = null;
                    return new ReaderResult(ExrResult.Corrupt, null, exception);
                }

                ClearPending();
            }

            operation = _deepBlockOperation;
            return null;
        }

        private DeepBlockOperation PrepareDeepSamplesOperation(
            int partIndex,
            int blockIndex,
            ReadOnlySpan<int> counts,
            IReadOnlyList<DeepChannelDestination> destinations)
        {
            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            ReaderPartData part = data.Parts[partIndex];
            if (blockIndex >= part.Offsets.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            if (!part.Header.IsDeep)
            {
                throw new NotSupportedException("Flat blocks require the flat block decode API.");
            }

            DeepBlockOperation operation = _deepBlockOperation ??
                throw new InvalidOperationException(
                    "DecodeDeepCounts must complete successfully for this block before samples are decoded.");
            if (operation.Info.PartIndex != partIndex ||
                operation.Info.BlockIndex != blockIndex ||
                !operation.CountsDecoded)
            {
                throw new InvalidOperationException(
                    "DecodeDeepCounts must complete successfully for this block before samples are decoded.");
            }

            operation.ValidateSamplesArguments(counts, destinations);
            return operation;
        }

        private ReaderResult? PrepareDeepSamplePayload(DeepBlockOperation operation)
        {
            ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
            if (lengthFailure.HasValue)
            {
                return lengthFailure.Value;
            }

            try
            {
                ReaderResult? validation = operation.ValidateKnownLength(knownLength);
                if (validation.HasValue)
                {
                    _deepBlockOperation = null;
                    return validation.Value;
                }

                operation.BeginSamples();
                return null;
            }
            catch (OutOfMemoryException exception)
            {
                _deepBlockOperation = null;
                return new ReaderResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (OverflowException exception)
            {
                _deepBlockOperation = null;
                return new ReaderResult(ExrResult.Corrupt, null, exception);
            }
        }

        private ReaderResult? AdvanceDeepCountsOperation(
            DeepBlockOperation operation,
            long? knownLength,
            Span<int> counts)
        {
            try
            {
                if (operation.CountsDecoded)
                {
                    return operation.CopyCounts(counts);
                }

                if (operation.HeaderComplete && !operation.HeaderValidated)
                {
                    ReaderResult? validation = operation.ValidateHeader(knownLength);
                    if (validation.HasValue)
                    {
                        _deepBlockOperation = null;
                        return validation.Value;
                    }
                }

                if (!operation.OffsetPayloadComplete)
                {
                    return null;
                }

                ReaderResult result = operation.DecodeCounts(counts);
                if (!result.IsSuccess)
                {
                    _deepBlockOperation = null;
                }

                return result;
            }
            catch (OutOfMemoryException exception)
            {
                _deepBlockOperation = null;
                return new ReaderResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is OverflowException)
            {
                _deepBlockOperation = null;
                return new ReaderResult(ExrResult.Corrupt, null, exception);
            }
        }

        private ReaderResult? AdvanceDeepSamplesOperation(
            DeepBlockOperation operation,
            IReadOnlyList<DeepChannelDestination> destinations)
        {
            if (!operation.SamplePayloadComplete)
            {
                return null;
            }

            try
            {
                ReaderResult result = operation.DecodeSamples(destinations);
                _deepBlockOperation = null;
                return result;
            }
            catch (OutOfMemoryException exception)
            {
                _deepBlockOperation = null;
                return new ReaderResult(ExrResult.OutOfMemory, null, exception);
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException ||
                exception is OverflowException)
            {
                _deepBlockOperation = null;
                return new ReaderResult(ExrResult.Corrupt, null, exception);
            }
        }

        private ReaderResult? HandleDeepTransferResult(
            DataTransferResult transfer,
            ReaderParserRequest request)
        {
            switch (transfer.Status)
            {
                case DataTransferStatus.Success:
                    if (!transfer.IsByteCountExact || transfer.BytesTransferred != request.Length)
                    {
                        return IoFailure(new IOException(
                            "An exact data source reported success without the requested exact byte count."));
                    }

                    return null;
                case DataTransferStatus.WouldBlock:
                    if (!transfer.PendingRange.HasValue ||
                        transfer.PendingRange.Value.Length <= 0 ||
                        transfer.PendingRange.Value.Offset < request.Offset ||
                        transfer.PendingRange.Value.End > checked(request.Offset + request.Length))
                    {
                        return IoFailure(new IOException(
                            "An exact data source returned an invalid pending range."));
                    }

                    return new ReaderResult(ExrResult.WouldBlock, transfer.PendingRange, null);
                case DataTransferStatus.EndOfSource:
                    _deepBlockOperation = null;
                    return new ReaderResult(
                        ExrResult.Corrupt,
                        null,
                        new EndOfStreamException("The source ended before the deep EXR block was complete."));
                case DataTransferStatus.Canceled:
                    throw transfer.Error as OperationCanceledException ?? new OperationCanceledException();
                case DataTransferStatus.Disposed:
                case DataTransferStatus.IoError:
                    return IoFailure(transfer.Error ?? new IOException("The exact data source failed."));
                default:
                    return IoFailure(new IOException("The exact data source returned an unknown status."));
            }
        }

        private ReaderResult DecodeBlockCore(
            int partIndex,
            int blockIndex,
            Span<byte> destination)
        {
            ReaderResult? setup = PrepareBlockOperation(
                partIndex,
                blockIndex,
                destination.Length,
                out FlatBlockOperation? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
            if (lengthFailure.HasValue)
            {
                return lengthFailure.Value;
            }

            for (;;)
            {
                ReaderParserRequest request;
                bool reconstructing = operation == null;
                if (reconstructing)
                {
                    ReaderResult? reconstruction = AdvanceOffsetReconstruction(knownLength);
                    if (reconstruction.HasValue)
                    {
                        return reconstruction.Value;
                    }

                    if (_offsetReconstruction == null)
                    {
                        setup = PrepareBlockOperation(
                            partIndex,
                            blockIndex,
                            destination.Length,
                            out operation);
                        if (setup.HasValue)
                        {
                            return setup.Value;
                        }

                        continue;
                    }

                    request = _offsetReconstruction.GetNextRequest();
                }
                else
                {
                    ReaderResult? completion = AdvanceBlockOperation(
                        operation!,
                        knownLength,
                        destination);
                    if (completion.HasValue)
                    {
                        return completion.Value;
                    }

                    request = operation!.GetNextRequest();
                }

                DataTransferResult transfer;
                try
                {
                    transfer = _syncSource!.ReadExactly(request.Offset, request.Span);
                }
                catch (ObjectDisposedException exception)
                {
                    return IoFailure(exception);
                }
                catch (IOException exception)
                {
                    return IoFailure(exception);
                }

                ReaderResult? transferResult = reconstructing
                    ? HandleOffsetReconstructionTransferResult(transfer, request)
                    : HandleBlockTransferResult(transfer, request);
                if (transferResult.HasValue)
                {
                    return transferResult.Value;
                }

                if (reconstructing)
                {
                    _offsetReconstruction!.AcceptRequest(request.Length);
                }
                else
                {
                    operation!.AcceptRequest(request.Length);
                }
            }
        }

        private async ValueTask<ReaderResult> DecodeBlockCoreAsync(
            int partIndex,
            int blockIndex,
            Memory<byte> destination,
            CancellationToken cancellationToken)
        {
            ReaderResult? setup = PrepareBlockOperation(
                partIndex,
                blockIndex,
                destination.Length,
                out FlatBlockOperation? operation);
            if (setup.HasValue)
            {
                return setup.Value;
            }

            ReaderResult? lengthFailure = TryGetKnownLength(out long? knownLength);
            if (lengthFailure.HasValue)
            {
                return lengthFailure.Value;
            }

            for (;;)
            {
                ReaderParserRequest request;
                bool reconstructing = operation == null;
                if (reconstructing)
                {
                    ReaderResult? reconstruction = AdvanceOffsetReconstruction(knownLength);
                    if (reconstruction.HasValue)
                    {
                        return reconstruction.Value;
                    }

                    if (_offsetReconstruction == null)
                    {
                        setup = PrepareBlockOperation(
                            partIndex,
                            blockIndex,
                            destination.Length,
                            out operation);
                        if (setup.HasValue)
                        {
                            return setup.Value;
                        }

                        continue;
                    }

                    request = _offsetReconstruction.GetNextRequest();
                }
                else
                {
                    ReaderResult? completion = AdvanceBlockOperation(
                        operation!,
                        knownLength,
                        destination.Span);
                    if (completion.HasValue)
                    {
                        return completion.Value;
                    }

                    request = operation!.GetNextRequest();
                }

                DataTransferResult transfer;
                try
                {
                    transfer = await _asyncSource!.ReadExactlyAsync(
                        request.Offset,
                        request.Memory,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ObjectDisposedException exception)
                {
                    return IoFailure(exception);
                }
                catch (IOException exception)
                {
                    return IoFailure(exception);
                }

                ReaderResult? transferResult = reconstructing
                    ? HandleOffsetReconstructionTransferResult(transfer, request)
                    : HandleBlockTransferResult(transfer, request);
                if (transferResult.HasValue)
                {
                    return transferResult.Value;
                }

                if (reconstructing)
                {
                    _offsetReconstruction!.AcceptRequest(request.Length);
                }
                else
                {
                    operation!.AcceptRequest(request.Length);
                }
            }
        }

        private ReaderResult? PrepareBlockOperation(
            int partIndex,
            int blockIndex,
            int destinationLength,
            out FlatBlockOperation? operation)
        {
            ReaderFileData data = GetReadyData();
            if (partIndex >= data.Parts.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(partIndex));
            }

            ReaderPartData part = data.Parts[partIndex];
            if (blockIndex >= part.Offsets.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex));
            }

            BlockInfo info = part.GetBlockInfo(blockIndex);
            if (info.IsDeep)
            {
                operation = null;
                return new ReaderResult(
                    ExrResult.Unsupported,
                    null,
                    new NotSupportedException("Deep blocks require the two-stage deep decode API."));
            }

            if (info.IsMissing)
            {
                operation = null;
                if (_offsetReconstruction == null)
                {
                    _blockOperation = null;
                    try
                    {
                        _offsetReconstruction = new ChunkOffsetReconstructionOperation(data, _limits);
                    }
                    catch (OutOfMemoryException exception)
                    {
                        return new ReaderResult(ExrResult.OutOfMemory, null, exception);
                    }
                    catch (OverflowException exception)
                    {
                        return new ReaderResult(ExrResult.Corrupt, null, exception);
                    }

                    ClearPending();
                }

                return null;
            }

            if (!info.UncompressedByteCount.HasValue || info.UncompressedByteCount.Value > int.MaxValue)
            {
                operation = null;
                return new ReaderResult(
                    ExrResult.Unsupported,
                    null,
                    new ReaderLimitExceededException(
                        nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                        long.MaxValue,
                        _limits.MaximumUncompressedBlockByteCount));
            }

            int required = (int)info.UncompressedByteCount.Value;
            if (required > _limits.MaximumUncompressedBlockByteCount)
            {
                operation = null;
                return new ReaderResult(
                    ExrResult.Unsupported,
                    null,
                    new ReaderLimitExceededException(
                        nameof(ReaderLimits.MaximumUncompressedBlockByteCount),
                        required,
                        _limits.MaximumUncompressedBlockByteCount));
            }

            if (destinationLength < required)
            {
                throw new ArgumentException(
                    $"The destination contains {destinationLength} bytes; {required} bytes are required.",
                    "destination");
            }

            if (_blockOperation == null ||
                _blockOperation.Info.PartIndex != partIndex ||
                _blockOperation.Info.BlockIndex != blockIndex)
            {
                _blockOperation = new FlatBlockOperation(part.Header, info, data.Multipart, _limits);
                ClearPending();
            }

            operation = _blockOperation;
            return null;
        }

        private ReaderResult? AdvanceBlockOperation(
            FlatBlockOperation operation,
            long? knownLength,
            Span<byte> destination)
        {
            if (operation.HeaderComplete && !operation.HeaderValidated)
            {
                ReaderResult? validation = operation.ValidateHeader(knownLength);
                if (validation.HasValue)
                {
                    _blockOperation = null;
                    return validation.Value;
                }
            }

            if (!operation.PayloadComplete)
            {
                return null;
            }

            ReaderResult result = operation.Decode(destination);
            _blockOperation = null;
            return result;
        }

        private ReaderResult? AdvanceOffsetReconstruction(long? knownLength)
        {
            ChunkOffsetReconstructionOperation operation = _offsetReconstruction ??
                throw new InvalidOperationException("No chunk offset reconstruction is active.");
            ReaderResult? result = operation.Advance(knownLength);
            if (result.HasValue)
            {
                _offsetReconstruction = null;
                return result.Value;
            }

            if (!operation.IsComplete)
            {
                return null;
            }

            ReaderFileData reconstructed = operation.ReconstructedData ??
                throw new InvalidOperationException("The completed reconstruction did not publish reader data.");
            lock (_stateGate)
            {
                if (!ReferenceEquals(_fileData, operation.SourceData))
                {
                    _offsetReconstruction = null;
                    return new ReaderResult(
                        ExrResult.IO,
                        null,
                        new InvalidOperationException("Reader metadata changed during chunk offset reconstruction."));
                }

                _fileData = reconstructed;
                _pending = null;
            }

            _offsetReconstruction = null;
            return null;
        }

        private ReaderResult? HandleOffsetReconstructionTransferResult(
            DataTransferResult transfer,
            ReaderParserRequest request)
        {
            switch (transfer.Status)
            {
                case DataTransferStatus.Success:
                    if (!transfer.IsByteCountExact || transfer.BytesTransferred != request.Length)
                    {
                        return IoFailure(new IOException(
                            "An exact data source reported success without the requested exact byte count."));
                    }

                    return null;
                case DataTransferStatus.WouldBlock:
                    if (!transfer.PendingRange.HasValue ||
                        transfer.PendingRange.Value.Length <= 0 ||
                        transfer.PendingRange.Value.Offset < request.Offset ||
                        transfer.PendingRange.Value.End > checked(request.Offset + request.Length))
                    {
                        return IoFailure(new IOException(
                            "An exact data source returned an invalid pending range."));
                    }

                    return new ReaderResult(ExrResult.WouldBlock, transfer.PendingRange, null);
                case DataTransferStatus.EndOfSource:
                    _offsetReconstruction = null;
                    return new ReaderResult(
                        ExrResult.Corrupt,
                        null,
                        new EndOfStreamException(
                            "The source ended before the chunk offset table could be reconstructed."));
                case DataTransferStatus.Canceled:
                    throw transfer.Error as OperationCanceledException ?? new OperationCanceledException();
                case DataTransferStatus.Disposed:
                case DataTransferStatus.IoError:
                    return IoFailure(transfer.Error ?? new IOException("The exact data source failed."));
                default:
                    return IoFailure(new IOException("The exact data source returned an unknown status."));
            }
        }

        private ReaderResult? HandleBlockTransferResult(
            DataTransferResult transfer,
            ReaderParserRequest request)
        {
            switch (transfer.Status)
            {
                case DataTransferStatus.Success:
                    if (!transfer.IsByteCountExact || transfer.BytesTransferred != request.Length)
                    {
                        return IoFailure(new IOException(
                            "An exact data source reported success without the requested exact byte count."));
                    }

                    return null;
                case DataTransferStatus.WouldBlock:
                    if (!transfer.PendingRange.HasValue ||
                        transfer.PendingRange.Value.Length <= 0 ||
                        transfer.PendingRange.Value.Offset < request.Offset ||
                        transfer.PendingRange.Value.End > checked(request.Offset + request.Length))
                    {
                        return IoFailure(new IOException(
                            "An exact data source returned an invalid pending range."));
                    }

                    return new ReaderResult(ExrResult.WouldBlock, transfer.PendingRange, null);
                case DataTransferStatus.EndOfSource:
                    _blockOperation = null;
                    return new ReaderResult(
                        ExrResult.Corrupt,
                        null,
                        new EndOfStreamException("The source ended before the EXR block was complete."));
                case DataTransferStatus.Canceled:
                    throw transfer.Error as OperationCanceledException ?? new OperationCanceledException();
                case DataTransferStatus.Disposed:
                case DataTransferStatus.IoError:
                    return IoFailure(transfer.Error ?? new IOException("The exact data source failed."));
                default:
                    return IoFailure(new IOException("The exact data source returned an unknown status."));
            }
        }

        private void ClearPending()
        {
            lock (_stateGate)
            {
                _pending = null;
            }
        }
    }
}
