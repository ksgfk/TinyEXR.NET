#include <benchmark/benchmark.h>

#include "exr.h"

#include <array>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace
{

constexpr int kWidth = 1920;
constexpr int kHeight = 1080;
constexpr int kChannelCount = 4;
constexpr int64_t kRawByteCount =
    static_cast<int64_t>(kWidth) * kHeight * kChannelCount * sizeof(uint16_t);
constexpr const char* kFixtureRelativePath =
    ".cache/compression-benchmarks/v3-managed";

struct CompressionCase
{
    const char* name;
    const char* fixture_name;
    exr_compression compression;
};

constexpr CompressionCase kCompressionCases[] = {
    {"None", "none.exr", EXR_COMPRESSION_NONE},
    {"RLE", "rle.exr", EXR_COMPRESSION_RLE},
    {"ZIPS", "zips.exr", EXR_COMPRESSION_ZIPS},
    {"ZIP", "zip.exr", EXR_COMPRESSION_ZIP},
    {"PIZ", "piz.exr", EXR_COMPRESSION_PIZ},
    {"PXR24", "pxr24.exr", EXR_COMPRESSION_PXR24},
    {"B44", "b44.exr", EXR_COMPRESSION_B44},
    {"B44A", "b44a.exr", EXR_COMPRESSION_B44A},
    {"HTJ2K256", "htj2k256.exr", EXR_COMPRESSION_HTJ2K256},
    {"HTJ2K32", "htj2k32.exr", EXR_COMPRESSION_HTJ2K32},
    {"ZSTD", "zstd.exr", EXR_COMPRESSION_ZSTD},
};

class EncodedBuffer final
{
public:
    EncodedBuffer(void* data, size_t size)
        : data_(data), size_(size)
    {
    }

    ~EncodedBuffer()
    {
        std::free(data_);
    }

    EncodedBuffer(const EncodedBuffer&) = delete;
    EncodedBuffer& operator=(const EncodedBuffer&) = delete;

    EncodedBuffer(EncodedBuffer&& other) noexcept
        : data_(std::exchange(other.data_, nullptr)),
          size_(std::exchange(other.size_, 0))
    {
    }

    [[nodiscard]] unsigned char* Data() const
    {
        return static_cast<unsigned char*>(data_);
    }

    [[nodiscard]] size_t Size() const
    {
        return size_;
    }

private:
    void* data_;
    size_t size_;
};

class SourceImage final
{
public:
    SourceImage()
    {
        const size_t pixel_count = static_cast<size_t>(kWidth) * kHeight;
        for (int c = 0; c < kChannelCount; c++)
        {
            channels_[c].resize(pixel_count);
        }

        FillChannels();

        const char* names[] = {"A", "B", "G", "R"};
        std::memset(&channels_info_, 0, sizeof(channels_info_));
        for (int c = 0; c < kChannelCount; c++)
        {
            channels_info_[c].name[0] = names[c][0];
            channels_info_[c].pixel_type = EXR_PIXEL_HALF;
            channels_info_[c].x_sampling = 1;
            channels_info_[c].y_sampling = 1;
        }

        std::memset(&part_, 0, sizeof(part_));
        part_.header.part_type = EXR_PART_SCANLINE;
        part_.header.compression = EXR_COMPRESSION_NONE;
        part_.header.line_order = EXR_LINEORDER_INCREASING_Y;
        part_.header.data_window.min_x = 0;
        part_.header.data_window.min_y = 0;
        part_.header.data_window.max_x = kWidth - 1;
        part_.header.data_window.max_y = kHeight - 1;
        part_.header.display_window = part_.header.data_window;
        part_.header.pixel_aspect_ratio = 1.0f;
        part_.header.screen_window_width = 1.0f;
        part_.header.num_channels = kChannelCount;
        part_.header.channels = channels_info_.data();
        part_.width = kWidth;
        part_.height = kHeight;
        for (int c = 0; c < kChannelCount; c++)
        {
            image_views_[c] = channels_[c].data();
        }
        part_.images = image_views_.data();

        std::memset(&image_, 0, sizeof(image_));
        image_.num_parts = 1;
        image_.parts = &part_;
    }

    [[nodiscard]] const exr_image& Image() const
    {
        return image_;
    }

private:
    [[nodiscard]] static uint16_t ChannelBits(char channel, int x, int y)
    {
        switch (channel)
        {
            case 'A':
                return 0x3C00;
            case 'B':
                return static_cast<uint16_t>(
                    0x3000 | (((x >> 2) + (3 * (y >> 2))) & 0x03FF));
            case 'G':
                return static_cast<uint16_t>(
                    0x3400 | ((y * 0x03FF) / (kHeight - 1)));
            case 'R':
                return static_cast<uint16_t>(
                    0x3800 | ((x * 0x03FF) / (kWidth - 1)));
            default:
                throw std::invalid_argument("Unknown benchmark channel.");
        }
    }

    void FillChannels()
    {
        const char* names[] = {"A", "B", "G", "R"};
        for (int c = 0; c < kChannelCount; c++)
        {
            uint16_t* data = channels_[c].data();
            const size_t pixel_count = static_cast<size_t>(kWidth) * kHeight;
            for (size_t i = 0; i < pixel_count; i++)
            {
                const int x = static_cast<int>(i % kWidth);
                const int y = static_cast<int>(i / kWidth);
                data[i] = ChannelBits(names[c][0], x, y);
            }
        }
    }

    exr_image image_{};
    exr_part part_{};
    std::array<exr_channel, kChannelCount> channels_info_{};
    std::array<std::vector<uint16_t>, kChannelCount> channels_;
    std::array<void*, kChannelCount> image_views_{};
};

[[nodiscard]] std::filesystem::path RepoRoot()
{
    return std::filesystem::path(TINYEXR_BENCHMARK_REPO_ROOT);
}

[[nodiscard]] std::vector<unsigned char> ReadAllBytes(const std::filesystem::path& path)
{
    std::ifstream stream(path, std::ios::binary);
    if (!stream)
    {
        throw std::runtime_error(
            "Shared V3 fixture was not found: " + path.generic_string() +
            ". Run the managed --prepare-v3-compression-fixtures command first.");
    }

    stream.seekg(0, std::ios::end);
    const std::streamoff length = stream.tellg();
    stream.seekg(0, std::ios::beg);
    if (length < 0 || static_cast<uint64_t>(length) > std::numeric_limits<size_t>::max())
    {
        throw std::runtime_error("Invalid fixture size: " + path.generic_string());
    }

    std::vector<unsigned char> bytes(static_cast<size_t>(length));
    if (!bytes.empty())
    {
        stream.read(
            reinterpret_cast<char*>(bytes.data()),
            static_cast<std::streamsize>(bytes.size()));
        if (!stream)
        {
            throw std::runtime_error("Unable to read fixture: " + path.generic_string());
        }
    }

    return bytes;
}

[[nodiscard]] std::string ErrorText(exr_result rc)
{
    const char* text = exr_result_string(rc);
    return text == nullptr ? std::string() : std::string(": ") + text;
}

[[nodiscard]] EncodedBuffer Encode(const SourceImage& source, exr_compression compression)
{
    void* data = nullptr;
    size_t size = 0;
    const exr_result rc = exr_save_to_memory(
        &data, &size, nullptr, &source.Image(), compression);
    if (!EXR_OK(rc) || data == nullptr || size == 0)
    {
        throw std::runtime_error("TinyEXR v3 encode failed" + ErrorText(rc));
    }

    return EncodedBuffer(data, size);
}

[[nodiscard]] int Decode(const std::vector<unsigned char>& encoded)
{
    exr_image decoded{};
    const exr_result rc = exr_load_from_memory(
        encoded.data(), encoded.size(), nullptr, &decoded);
    if (!EXR_OK(rc))
    {
        throw std::runtime_error("TinyEXR v3 decode failed" + ErrorText(rc));
    }

    if (decoded.num_parts != 1 || decoded.parts == nullptr ||
        decoded.parts[0].width != kWidth || decoded.parts[0].height != kHeight ||
        decoded.parts[0].header.num_channels != kChannelCount)
    {
        exr_image_free(&decoded);
        throw std::runtime_error("TinyEXR v3 decoded unexpected benchmark geometry.");
    }

    const exr_part& part = decoded.parts[0];
    const int result = part.width + part.height + part.header.num_channels;
    exr_image_free(&decoded);
    return result;
}

class CompressionRepository final
{
public:
    static CompressionRepository& Instance()
    {
        static CompressionRepository repository;
        return repository;
    }

    [[nodiscard]] const SourceImage& Source() const
    {
        return source_;
    }

    [[nodiscard]] const std::vector<unsigned char>& DecodeInput(size_t index) const
    {
        return decode_inputs_.at(index);
    }

    [[nodiscard]] size_t NativeEncodedSize(size_t index) const
    {
        return native_encoded_sizes_.at(index);
    }

private:
    CompressionRepository()
    {
        decode_inputs_.reserve(std::size(kCompressionCases));
        native_encoded_sizes_.reserve(std::size(kCompressionCases));
        for (const CompressionCase& compression_case : kCompressionCases)
        {
            EncodedBuffer encoded = Encode(source_, compression_case.compression);
            native_encoded_sizes_.push_back(encoded.Size());

            const std::filesystem::path fixture =
                RepoRoot() / kFixtureRelativePath / compression_case.fixture_name;
            decode_inputs_.push_back(ReadAllBytes(fixture));
            int validation = Decode(decode_inputs_.back());
            benchmark::DoNotOptimize(validation);
        }
    }

    SourceImage source_;
    std::vector<std::vector<unsigned char>> decode_inputs_;
    std::vector<size_t> native_encoded_sizes_;
};

void SetCounters(benchmark::State& state, size_t encoded_byte_count, bool shared_input)
{
    state.SetBytesProcessed(state.iterations() * kRawByteCount);
    state.counters["EncodedBytes"] = benchmark::Counter(
        static_cast<double>(encoded_byte_count),
        benchmark::Counter::kDefaults,
        benchmark::Counter::kIs1024);
    state.counters["CompressionRatio"] =
        static_cast<double>(kRawByteCount) / encoded_byte_count;
    state.counters["SharedInput"] = shared_input ? 1.0 : 0.0;
}

void BM_Encode(benchmark::State& state, size_t index)
{
    CompressionRepository& repository = CompressionRepository::Instance();
    const CompressionCase& compression_case = kCompressionCases[index];
    for (auto _ : state)
    {
        (void)_;
        void* data = nullptr;
        size_t size = 0;

        const auto started_at = std::chrono::steady_clock::now();
        const exr_result rc = exr_save_to_memory(
            &data,
            &size,
            nullptr,
            &repository.Source().Image(),
            compression_case.compression);
        const auto finished_at = std::chrono::steady_clock::now();
        state.SetIterationTime(
            std::chrono::duration<double>(finished_at - started_at).count());

        if (!EXR_OK(rc) || data == nullptr || size == 0)
        {
            std::free(data);
            const std::string error = "TinyEXR v3 encode failed" + ErrorText(rc);
            state.SkipWithError(error.c_str());
            break;
        }

        benchmark::DoNotOptimize(data);
        benchmark::DoNotOptimize(size);
        benchmark::ClobberMemory();
        std::free(data);
    }

    SetCounters(state, repository.NativeEncodedSize(index), false);
}

void BM_Decode(benchmark::State& state, size_t index)
{
    CompressionRepository& repository = CompressionRepository::Instance();
    const std::vector<unsigned char>& encoded = repository.DecodeInput(index);
    for (auto _ : state)
    {
        (void)_;
        exr_image decoded{};

        const auto started_at = std::chrono::steady_clock::now();
        const exr_result rc = exr_load_from_memory(
            encoded.data(), encoded.size(), nullptr, &decoded);
        const auto finished_at = std::chrono::steady_clock::now();
        state.SetIterationTime(
            std::chrono::duration<double>(finished_at - started_at).count());

        if (!EXR_OK(rc))
        {
            exr_image_free(&decoded);
            const std::string error = "TinyEXR v3 decode failed" + ErrorText(rc);
            state.SkipWithError(error.c_str());
            break;
        }

        if (decoded.num_parts != 1 || decoded.parts == nullptr ||
            decoded.parts[0].width != kWidth || decoded.parts[0].height != kHeight ||
            decoded.parts[0].header.num_channels != kChannelCount)
        {
            exr_image_free(&decoded);
            state.SkipWithError("TinyEXR v3 decoded unexpected benchmark geometry.");
            break;
        }

        benchmark::DoNotOptimize(decoded.parts[0].images);
        benchmark::ClobberMemory();
        exr_image_free(&decoded);
    }

    SetCounters(state, encoded.size(), true);
}

void RegisterBenchmarks()
{
    for (size_t index = 0; index < std::size(kCompressionCases); index++)
    {
        const std::string encode_name =
            std::string("TinyEXR/Encode/") + kCompressionCases[index].name;
        const std::string decode_name =
            std::string("TinyEXR/Decode/") + kCompressionCases[index].name;
        benchmark::RegisterBenchmark(encode_name.c_str(), &BM_Encode, index)
            ->Unit(benchmark::kMillisecond)
            ->UseManualTime();
        benchmark::RegisterBenchmark(decode_name.c_str(), &BM_Decode, index)
            ->Unit(benchmark::kMillisecond)
            ->UseManualTime();
    }
}

}  // namespace

int main(int argc, char** argv)
{
    benchmark::AddCustomContext("Compiler", TINYEXR_BENCHMARK_COMPILER);
    benchmark::AddCustomContext("TinyEXR", TINYEXR_BENCHMARK_VERSION);
    benchmark::AddCustomContext(
        "Optimization",
        TINYEXR_BENCHMARK_OPTIMIZATION);
    benchmark::AddCustomContext("DEFLATE", TINYEXR_BENCHMARK_DEFLATE);
    benchmark::AddCustomContext(
        "Image",
        "1920x1080 RGBA HALF, single-threaded, memory-only");
    benchmark::AddCustomContext(
        "Timing",
        "complete result allocation and exr_save/load; validation/free excluded");

    RegisterBenchmarks();
    benchmark::Initialize(&argc, argv);
    if (benchmark::ReportUnrecognizedArguments(argc, argv))
    {
        return 1;
    }

    benchmark::RunSpecifiedBenchmarks();
    benchmark::Shutdown();
    return 0;
}
