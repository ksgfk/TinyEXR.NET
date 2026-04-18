using TinyEXR;

float[] rgba =
[
    1.0f, 0.25f, 0.5f, 1.0f
];

ResultCode saveResult = Exr.SaveEXRToMemory(rgba, 1, 1, 4, false, out byte[] encoded);
if (saveResult != ResultCode.Success)
{
    Console.Error.WriteLine($"SaveEXRToMemory failed: {saveResult}");
    return 1;
}

if (encoded.Length == 0)
{
    Console.Error.WriteLine("SaveEXRToMemory returned an empty payload.");
    return 2;
}

if (!Exr.IsExrFromMemory(encoded))
{
    Console.Error.WriteLine("IsExrFromMemory rejected the encoded payload.");
    return 3;
}

Console.WriteLine("ok");
return 0;
