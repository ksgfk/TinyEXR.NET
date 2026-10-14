using System.Buffers.Binary;
using V3 = TinyEXR;
namespace TinyEXR.Test;
[TestClass]
public sealed class B44RawFallbackTests
{
    [TestMethod(DisplayName = "B44/B44A native edge tile raw fallback avoids codec staging")]
    public void Case_B44NativeRawEdgeTile()
    {
        // OpenEXR 3.5.2: 7x5 HALF R, negative data-window origin, 4x3 tiles.
        // The bottom-right 3x2 tile has a 12-byte raw payload; encoded B44 would require 14 bytes.
        foreach (string encoded in new[] { "di8xAQICAABjaGFubmVscwBjaGxpc3QAEwAAAFIAAQAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAABmRhdGFXaW5kb3cAYm94MmkAEAAAAPz////9////AgAAAAEAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAD8/////f///wIAAAABAAAAbGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/dGlsZXMAdGlsZWRlc2MACQAAAAQAAAADAAAAAABRAQAAAAAAAHMBAAAAAAAAlQEAAAAAAAC3AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOAAAAQQAr4SCGCGGCG22GC20BAAAAAAAAAAAAAAAAAAAADgAAAEwAKe9gtghhtgggggggAAAAAAEAAAAAAAAAAAAAAA4AAAC9ACnoIAYYYYYIIIIYYQEAAAABAAAAAAAAAAAAAAAMAAAAALgAtAAAAD0AvQC8", "di8xAQICAABjaGFubmVscwBjaGxpc3QAEwAAAFIAAQAAAAAAAAABAAAAAQAAAABjb21wcmVzc2lvbgBjb21wcmVzc2lvbgABAAAAB2RhdGFXaW5kb3cAYm94MmkAEAAAAPz////9////AgAAAAEAAABkaXNwbGF5V2luZG93AGJveDJpABAAAAD8/////f///wIAAAABAAAAbGluZU9yZGVyAGxpbmVPcmRlcgABAAAAAHBpeGVsQXNwZWN0UmF0aW8AZmxvYXQABAAAAAAAgD9zY3JlZW5XaW5kb3dDZW50ZXIAdjJmAAgAAAAAAAAAAAAAAHNjcmVlbldpbmRvd1dpZHRoAGZsb2F0AAQAAAAAAIA/dGlsZXMAdGlsZWRlc2MACQAAAAQAAAADAAAAAABRAQAAAAAAAHMBAAAAAAAAlQEAAAAAAAC3AQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOAAAAQQAr4SCGCGGCG22GC20BAAAAAAAAAAAAAAAAAAAADgAAAEwAKe9gtghhtgggggggAAAAAAEAAAAAAAAAAAAAAA4AAAC9ACnoIAYYYYYIIIIYYQEAAAABAAAAAAAAAAAAAAAMAAAAALgAtAAAAD0AvQC8" })
        {
            var result = V3.ExrFile.LoadFromMemory(Convert.FromBase64String(encoded));
            Assert.AreEqual(V3.ExrResult.Success, result.Status, result.Error?.ToString());
            Assert.IsNotNull(result.Value);
            var level = result.Value.Parts[0].GetLevel(0, 0);
            var samples = level.GetChannel("R").Data;
            Assert.AreEqual(7L, level.Width);
            Assert.AreEqual(5L, level.Height);
            // Native raw corner contains the original row-major values at x=4..6, y=3..4.
            for (int y = 3; y < 5; y++) for (int x = 4; x < 7; x++)
            {
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(samples.Slice((y * 7 + x) * 2));
                Assert.AreEqual((Half)(((y * 7 + x) % 11 - 5) * 0.25f), BitConverter.UInt16BitsToHalf(bits));
            }
        }
    }
}
