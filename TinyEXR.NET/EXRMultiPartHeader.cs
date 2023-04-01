namespace TinyEXR
{
    public unsafe partial struct EXRMultiPartHeader
    {
        public int num_headers;

        public EXRHeader* headers;
    }
}