// Port of tinyexr v3 src/exr_zip.c, revision 1b106618644dbf8a0935c2348ba51a2d863dd7c2.
// Copyright (c) 2014-2026 Syoyo Fujita and TinyEXR authors. SPDX-License-Identifier: BSD-3-Clause
namespace TinyEXR.Codecs
{
    internal static class ZipCodec
    {
        public static ExrResult Decode(ReadOnlySpan<byte> source, Span<byte> destination, CodecWorkspace workspace)
        {
            Span<byte> predicted = workspace.Bytes(0, destination.Length).AsSpan(0, destination.Length);
            ExrResult result = ZlibBackend.Inflate(source, predicted);
            if (result == ExrResult.Success)
                ExrPredictor.Undo(predicted, predicted.Length, destination);
            return result;
        }

        public static byte[] Encode(ReadOnlySpan<byte> source, CodecWorkspace workspace)
        {
            if (source.IsEmpty)
                return Array.Empty<byte>();
            Span<byte> predicted = workspace.Bytes(0, source.Length).AsSpan(0, source.Length);
            ExrPredictor.Apply(source, predicted);
            return ZlibBackend.Deflate(predicted);
        }
    }
}
