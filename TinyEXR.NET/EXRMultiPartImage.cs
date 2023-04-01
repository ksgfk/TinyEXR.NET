namespace TinyEXR
{
    public unsafe partial struct EXRMultiPartImage
    {
        public int num_images;

        public EXRImage* images;
    }
}