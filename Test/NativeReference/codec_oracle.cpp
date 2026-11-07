// Test-only adapter to the unmodified, pinned official OpenEXR compressor.
#include <ImfHeader.h>
#include <ImfChannelList.h>
#include <ImfCompressor.h>
#include <fstream>
#include <iostream>
#include <iterator>
#include <vector>
#include <cstdio>
#include <memory>
using namespace OPENEXR_IMF_NAMESPACE;
using namespace IMATH_NAMESPACE;
class BlockProbe : public Compressor
{
public:
    BlockProbe(const Header& header, Compression compression, size_t rowSize, int rows)
        : Compressor(header, static_cast<exr_compression_t>(compression), rowSize, rows) {}

    std::vector<char> run(bool encode, Box2i region, std::vector<char>& input, int rawSize)
    {
        exr_chunk_info_t info{};
        if (exr_chunk_default_initialize(_ctxt, 0, reinterpret_cast<const exr_attr_box2i_t*>(&region), 0, 0, &info) != EXR_ERR_SUCCESS)
            throw std::runtime_error("chunk initialization");
        // Algorithm probes supply explicit block geometry. Full-file probes
        // separately enforce compression-specific scanline chunk heights.
        info.height = region.max.y - region.min.y + 1;
        info.width = region.max.x - region.min.x + 1;
        info.unpacked_size = rawSize;
        info.packed_size = input.size();
        std::vector<char> output;
        exr_result_t result;
        if (encode)
        {
            if (exr_encoding_initialize(_ctxt, 0, &info, &_encoder) != EXR_ERR_SUCCESS)
                throw std::runtime_error("encoder initialization");
            _encoder_init = true;
            _encoder.packed_buffer = input.data();
            _encoder.packed_bytes = input.size();
            result = exr_compress_chunk(&_encoder);
            _encoder.packed_buffer = nullptr;
            _encoder.packed_bytes = 0;
            if (result == EXR_ERR_SUCCESS)
                output.assign(static_cast<char*>(_encoder.compressed_buffer), static_cast<char*>(_encoder.compressed_buffer) + _encoder.compressed_bytes);
        }
        else
        {
            if (exr_decoding_initialize(_ctxt, 0, &info, &_decoder) != EXR_ERR_SUCCESS)
                throw std::runtime_error("decoder initialization");
            _decoder_init = true;
            output.resize(rawSize);
            _decoder.packed_buffer = input.data();
            _decoder.unpacked_buffer = output.data();
            _decoder.unpacked_alloc_size = output.size();
            result = exr_uncompress_chunk(&_decoder);
            _decoder.packed_buffer = nullptr;
            _decoder.unpacked_buffer = nullptr;
            _decoder.unpacked_alloc_size = 0;
            if (result == EXR_ERR_SUCCESS) output.resize(_decoder.bytes_decompressed);
        }
        if (result != EXR_ERR_SUCCESS) throw std::runtime_error("official block codec failed");
        return output;
    }
};
int main(int argc, char** argv)
{
    try
    {
        if (argc < 11) throw std::runtime_error("mode input output compression x y width height rawSize channels...");
        bool encode = std::string(argv[1]) == "encode";
        auto compression = static_cast<Compression>(std::stoi(argv[4]));
        int x = std::stoi(argv[5]), y = std::stoi(argv[6]), w = std::stoi(argv[7]), h = std::stoi(argv[8]);
        int rawSize = std::stoi(argv[9]);
        Box2i region(V2i(x, y), V2i(x + w - 1, y + h - 1));
        Header header(region, region);
        header.compression() = compression;
        size_t rowSize = 0;
        for (int c = 10; c < argc; ++c)
        {
            int type, xs, ys, linear;
            if (sscanf(argv[c], "%d:%d:%d:%d", &type, &xs, &ys, &linear) != 4) throw std::runtime_error("channel");
            header.channels().insert(std::string(1, char('A' + c - 10)), Channel(static_cast<PixelType>(type), xs, ys, linear != 0));
            rowSize += ((w + xs - 1) / xs) * (type == 1 ? 2 : 4);
        }
        std::ifstream input(argv[2], std::ios::binary);
        std::vector<char> bytes((std::istreambuf_iterator<char>(input)), {});
        std::ofstream output(argv[3], std::ios::binary);
        if (!input || !output) throw std::runtime_error("I/O");
        if (compression == NO_COMPRESSION || (!encode && bytes.size() == size_t(rawSize)))
            output.write(bytes.data(), bytes.size());
        else
        {
            BlockProbe codec(header, compression, rowSize, h);
            auto result = codec.run(encode, region, bytes, rawSize);
            if (encode && result.size() >= size_t(rawSize)) output.write(bytes.data(), bytes.size());
            else output.write(result.data(), result.size());
        }
        if (!output) throw std::runtime_error("write");
        return 0;
    }
    catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
