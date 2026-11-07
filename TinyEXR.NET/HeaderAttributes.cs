using System.Buffers.Binary;

namespace TinyEXR
{
    public sealed partial class Header
    {
        public Header WithIntAttribute(string name, int value)
        {
            byte[] data = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(data, value);
            return WithCustomAttribute(new HeaderAttribute(name, "int", data));
        }

        public bool TryGetIntAttribute(string name, out int value)
        {
            HeaderAttribute? attribute = FindAttribute(name, "int");
            value = attribute?.ByteLength == 4 ? BinaryPrimitives.ReadInt32LittleEndian(attribute.Data) : 0;
            return attribute?.ByteLength == 4;
        }

        public Header WithFloatAttribute(string name, float value)
        {
            byte[] data = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(data, BitConverter.SingleToInt32Bits(value));
            return WithCustomAttribute(new HeaderAttribute(name, "float", data));
        }

        public bool TryGetFloatAttribute(string name, out float value)
        {
            HeaderAttribute? attribute = FindAttribute(name, "float");
            value = attribute?.ByteLength == 4 ? BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(attribute.Data)) : 0;
            return attribute?.ByteLength == 4;
        }

        public Header WithPreviewImage(PreviewImage preview)
        {
            if (preview == null) throw new ArgumentNullException(nameof(preview));
            return WithCustomAttribute(new HeaderAttribute("preview", "preview", preview.Encode()));
        }

        public bool TryGetPreviewImage(out PreviewImage? preview)
        {
            preview = null;
            HeaderAttribute? attribute = FindAttribute("preview", "preview");
            if (attribute == null || !PreviewImage.IsValid(attribute.Data)) return false;
            preview = new PreviewImage((int)BinaryPrimitives.ReadUInt32LittleEndian(attribute.Data),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(attribute.Data.Slice(4)), attribute.Data.Slice(8));
            return true;
        }

        internal Header Copy(string? name = null, IEnumerable<HeaderAttribute>? attributes = null) =>
            new Header(PartType, DataWindow, Channels, Compression, LineOrder, DisplayWindow,
                PixelAspectRatio, ScreenWindowCenter, ScreenWindowWidth, Tiles, name ?? Name, Chromaticities, attributes ?? Attributes);

        private Header WithCustomAttribute(HeaderAttribute attribute)
        {
            if (WriterPlanBuilder.ReservedAttributeNames.Contains(attribute.Name))
                throw new ArgumentException("Use the modeled header field for a reserved attribute.", nameof(attribute));
            List<HeaderAttribute> attributes = new List<HeaderAttribute>();
            foreach (HeaderAttribute existing in Attributes)
                if (existing.Name != attribute.Name) attributes.Add(existing);
            attributes.Add(attribute);
            return Copy(attributes: attributes);
        }

        private HeaderAttribute? FindAttribute(string name, string type)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            foreach (HeaderAttribute attribute in Attributes)
                if (attribute.Name == name) return attribute.TypeName == type ? attribute : null;
            return null;
        }
    }
}
