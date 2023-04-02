namespace TinyEXR.Native
{
    public unsafe partial struct EXRMultiPartImage
    {
        public int num_images;

        public EXRImage* images;
    }
}