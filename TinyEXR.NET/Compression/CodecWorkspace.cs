namespace TinyEXR.Codecs
{
    /// <summary>Scratch storage owned by a single reader or writer, never shared globally.</summary>
    internal sealed class CodecWorkspace
    {
        private byte[] _raw = Array.Empty<byte>();
        private readonly byte[][] _bytes = new byte[4][];
        private readonly ushort[][] _words = new ushort[3][];
        private PizHuffman.Workspace? _huffman;
        public PizHuffman.Workspace Huffman => _huffman ??= new PizHuffman.Workspace();

        public byte[] GetRaw(int length) => Grow(ref _raw, length);
        public byte[] Bytes(int slot, int length) => Grow(ref _bytes[slot], length);
        public ushort[] Words(int slot, int length) => Grow(ref _words[slot], length);

        private static T[] Grow<T>(ref T[] buffer, int length)
        {
            if (buffer == null || buffer.Length < length)
            {
                buffer = new T[length];
            }
            return buffer;
        }
    }
}
