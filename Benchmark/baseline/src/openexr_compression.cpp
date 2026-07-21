#include <benchmark/benchmark.h>

#include <ImfCompression.h>
#include <ImfHeader.h>
#include <ImfIO.h>
#include <ImfRgbaFile.h>
#include <openexr_version.h>

#include <algorithm>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace
{

namespace exr = OPENEXR_IMF_NAMESPACE;

constexpr int kWidth = 1920;
constexpr int kHeight = 1080;
constexpr int64_t kRawByteCount =
    static_cast<int64_t>(kWidth) * kHeight * 4 * sizeof(uint16_t);
constexpr size_t kOutputBufferByteCount =
    static_cast<size_t>(kRawByteCount) * 2;
constexpr const char* kFixtureRelativePath =
    ".cache/compression-benchmarks/v3-managed";

struct CompressionCase
{
    const char* name;
    const char* fixture_name;
    exr::Compression compression;
    bool has_managed_fixture;
};

constexpr CompressionCase kCompressionCases[] = {
    {"None", "none.exr", exr::NO_COMPRESSION, true},
    {"RLE", "rle.exr", exr::RLE_COMPRESSION, true},
    {"ZIPS", "zips.exr", exr::ZIPS_COMPRESSION, true},
    {"ZIP", "zip.exr", exr::ZIP_COMPRESSION, true},
    {"PIZ", "piz.exr", exr::PIZ_COMPRESSION, true},
    {"PXR24", "pxr24.exr", exr::PXR24_COMPRESSION, true},
    {"B44", "b44.exr", exr::B44_COMPRESSION, true},
    {"B44A", "b44a.exr", exr::B44A_COMPRESSION, true},
    {"DWAA", nullptr, exr::DWAA_COMPRESSION, false},
    {"DWAB", nullptr, exr::DWAB_COMPRESSION, false},
    {"HTJ2K256", "htj2k256.exr", exr::HTJ2K256_COMPRESSION, true},
    {"HTJ2K32", "htj2k32.exr", exr::HTJ2K32_COMPRESSION, true},
};

class MemoryOutputStream final : public exr::OStream
{
public:
    MemoryOutputStream()
        : exr::OStream("<memory>")
    {
    }

    void Allocate(size_t capacity)
    {
        if (data_ != nullptr)
        {
            throw std::logic_error("OpenEXR output buffer was not released.");
        }

        data_.reset(new char[capacity]);
        capacity_ = capacity;
        position_ = 0;
        size_ = 0;
    }

    void write(const char bytes[], int count) override
    {
        if (count < 0)
        {
            throw std::invalid_argument("OpenEXR requested a negative write size.");
        }

        const uint64_t end = CheckedEnd(static_cast<uint64_t>(count));
        if (end > capacity_)
        {
            throw std::length_error("OpenEXR output exceeds the preallocated memory stream.");
        }

        std::memcpy(data_.get() + position_, bytes, static_cast<size_t>(count));
        position_ = end;
        size_ = std::max(size_, end);
    }

    uint64_t tellp() override
    {
        return position_;
    }

    void seekp(uint64_t position) override
    {
        if (position > capacity_)
        {
            throw std::out_of_range("OpenEXR output seek exceeds the memory stream limit.");
        }

        position_ = position;
    }

    void Release()
    {
        data_.reset();
        capacity_ = 0;
        position_ = 0;
        size_ = 0;
    }

    [[nodiscard]] const char* Data() const
    {
        return data_.get();
    }

    [[nodiscard]] size_t Size() const
    {
        return static_cast<size_t>(size_);
    }

    [[nodiscard]] std::vector<char> CopyData() const
    {
        return std::vector<char>(data_.get(), data_.get() + Size());
    }

private:
    [[nodiscard]] uint64_t CheckedEnd(uint64_t count) const
    {
        if (count > std::numeric_limits<uint64_t>::max() - position_)
        {
            throw std::overflow_error("OpenEXR output position overflowed.");
        }

        return position_ + count;
    }

    std::unique_ptr<char[]> data_;
    size_t capacity_ = 0;
    uint64_t position_ = 0;
    uint64_t size_ = 0;
};

class MemoryInputStream final : public exr::IStream
{
public:
    explicit MemoryInputStream(const std::vector<char>& data)
        : exr::IStream("<memory>"), data_(data)
    {
    }

    bool isMemoryMapped() const override
    {
        return true;
    }

    char* readMemoryMapped(int count) override
    {
        const uint64_t start = ReadRange(count);
        return const_cast<char*>(data_.data() + start);
    }

    bool read(char destination[], int count) override
    {
        const uint64_t start = ReadRange(count);
        std::memcpy(destination, data_.data() + start, static_cast<size_t>(count));
        return position_ < data_.size();
    }

    uint64_t tellg() override
    {
        return position_;
    }

    void seekg(uint64_t position) override
    {
        if (position > data_.size())
        {
            throw std::out_of_range("OpenEXR input seek exceeds the memory stream.");
        }

        position_ = position;
    }

    int64_t size() override
    {
        return static_cast<int64_t>(data_.size());
    }

    void Reset()
    {
        position_ = 0;
    }

private:
    [[nodiscard]] uint64_t ReadRange(int count)
    {
        if (count < 0)
        {
            throw std::invalid_argument("OpenEXR requested a negative read size.");
        }

        const uint64_t byte_count = static_cast<uint64_t>(count);
        if (byte_count > data_.size() - position_)
        {
            throw std::out_of_range("OpenEXR read exceeds the memory stream.");
        }

        const uint64_t start = position_;
        position_ += byte_count;
        return start;
    }

    const std::vector<char>& data_;
    uint64_t position_ = 0;
};

[[nodiscard]] std::filesystem::path RepoRoot()
{
    return std::filesystem::path(TINYEXR_BENCHMARK_REPO_ROOT);
}

[[nodiscard]] std::vector<char> ReadAllBytes(const std::filesystem::path& path)
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
    if (length < 0)
    {
        throw std::runtime_error("Unable to determine fixture size: " + path.generic_string());
    }

    std::vector<char> bytes(static_cast<size_t>(length));
    if (!bytes.empty())
    {
        stream.read(bytes.data(), static_cast<std::streamsize>(bytes.size()));
        if (!stream)
        {
            throw std::runtime_error("Unable to read fixture: " + path.generic_string());
        }
    }

    return bytes;
}

[[nodiscard]] uint16_t ChannelBits(char channel, int x, int y)
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

[[nodiscard]] std::vector<exr::Rgba> CreatePixels()
{
    std::vector<exr::Rgba> pixels(static_cast<size_t>(kWidth) * kHeight);
    for (int y = 0; y < kHeight; y++)
    {
        for (int x = 0; x < kWidth; x++)
        {
            exr::Rgba& pixel = pixels[static_cast<size_t>(y) * kWidth + x];
            pixel.r.setBits(ChannelBits('R', x, y));
            pixel.g.setBits(ChannelBits('G', x, y));
            pixel.b.setBits(ChannelBits('B', x, y));
            pixel.a.setBits(ChannelBits('A', x, y));
        }
    }

    return pixels;
}

[[nodiscard]] std::vector<char> Encode(
    const std::vector<exr::Rgba>& pixels,
    exr::Compression compression)
{
    MemoryOutputStream stream;
    stream.Allocate(kOutputBufferByteCount);
    {
        exr::Header header(
            kWidth,
            kHeight,
            1.0F,
            IMATH_NAMESPACE::V2f(0.0F, 0.0F),
            1.0F,
            exr::INCREASING_Y,
            compression);
        exr::RgbaOutputFile output(stream, header, exr::WRITE_RGBA, 1);
        output.setFrameBuffer(pixels.data(), 1, kWidth);
        output.writePixels(kHeight);
    }

    return stream.CopyData();
}

[[nodiscard]] std::vector<exr::Rgba> Decode(const std::vector<char>& encoded)
{
    MemoryInputStream stream(encoded);
    exr::RgbaInputFile input(stream, 1);
    const IMATH_NAMESPACE::Box2i& window = input.dataWindow();
    if (window.min.x != 0 || window.min.y != 0 ||
        window.max.x != kWidth - 1 || window.max.y != kHeight - 1)
    {
        throw std::runtime_error("OpenEXR decoded unexpected benchmark geometry.");
    }

    std::vector<exr::Rgba> pixels(static_cast<size_t>(kWidth) * kHeight);
    input.setFrameBuffer(pixels.data(), 1, kWidth);
    input.readPixels(window.min.y, window.max.y);
    return pixels;
}

class CompressionRepository
{
public:
    static CompressionRepository& Instance()
    {
        static CompressionRepository repository;
        return repository;
    }

    [[nodiscard]] const std::vector<exr::Rgba>& Pixels() const
    {
        return pixels_;
    }

    [[nodiscard]] const std::vector<char>& NativeEncoded(size_t index) const
    {
        return native_encoded_.at(index);
    }

    [[nodiscard]] const std::vector<char>& DecodeInput(size_t index) const
    {
        return decode_inputs_.at(index);
    }

private:
    CompressionRepository()
        : pixels_(CreatePixels())
    {
        native_encoded_.reserve(std::size(kCompressionCases));
        decode_inputs_.reserve(std::size(kCompressionCases));
        for (const CompressionCase& compression_case : kCompressionCases)
        {
            native_encoded_.push_back(Encode(pixels_, compression_case.compression));
            if (compression_case.has_managed_fixture)
            {
                const std::filesystem::path fixture =
                    RepoRoot() / kFixtureRelativePath / compression_case.fixture_name;
                decode_inputs_.push_back(ReadAllBytes(fixture));
            }
            else
            {
                decode_inputs_.push_back(native_encoded_.back());
            }

            const std::vector<exr::Rgba> decoded = Decode(decode_inputs_.back());
            if (decoded.size() != pixels_.size())
            {
                throw std::runtime_error(
                    std::string("OpenEXR validation failed for ") + compression_case.name);
            }
        }
    }

    std::vector<exr::Rgba> pixels_;
    std::vector<std::vector<char>> native_encoded_;
    std::vector<std::vector<char>> decode_inputs_;
};

void SetCounters(
    benchmark::State& state,
    size_t encoded_byte_count,
    bool shared_managed_fixture)
{
    state.SetBytesProcessed(state.iterations() * kRawByteCount);
    state.counters["EncodedBytes"] = benchmark::Counter(
        static_cast<double>(encoded_byte_count),
        benchmark::Counter::kDefaults,
        benchmark::Counter::kIs1024);
    state.counters["CompressionRatio"] =
        static_cast<double>(kRawByteCount) / encoded_byte_count;
    state.counters["SharedInput"] = shared_managed_fixture ? 1.0 : 0.0;
}

void BM_Encode(benchmark::State& state, size_t index)
{
    CompressionRepository& repository = CompressionRepository::Instance();
    const CompressionCase& compression_case = kCompressionCases[index];
    const exr::Header header(
        kWidth,
        kHeight,
        1.0F,
        IMATH_NAMESPACE::V2f(0.0F, 0.0F),
        1.0F,
        exr::INCREASING_Y,
        compression_case.compression);
    MemoryOutputStream stream;
    const size_t encoded_byte_count = repository.NativeEncoded(index).size();
    for (auto _ : state)
    {
        (void)_;

        const auto started_at = std::chrono::steady_clock::now();
        stream.Allocate(encoded_byte_count);
        {
            exr::RgbaOutputFile output(stream, header, exr::WRITE_RGBA, 1);
            output.setFrameBuffer(repository.Pixels().data(), 1, kWidth);
            output.writePixels(kHeight);
        }
        const auto finished_at = std::chrono::steady_clock::now();
        state.SetIterationTime(
            std::chrono::duration<double>(finished_at - started_at).count());

        const bool size_matches = stream.Size() == encoded_byte_count;
        benchmark::DoNotOptimize(stream.Data());
        benchmark::DoNotOptimize(stream.Size());
        benchmark::ClobberMemory();
        stream.Release();

        if (!size_matches)
        {
            state.SkipWithError("OpenEXR encoded an unexpected byte count.");
            break;
        }
    }

    SetCounters(
        state,
        repository.NativeEncoded(index).size(),
        false);
}

void BM_Decode(benchmark::State& state, size_t index)
{
    CompressionRepository& repository = CompressionRepository::Instance();
    const std::vector<char>& encoded = repository.DecodeInput(index);
    MemoryInputStream stream(encoded);
    const size_t pixel_count = static_cast<size_t>(kWidth) * kHeight;
    std::unique_ptr<exr::Rgba[]> pixels;
    for (auto _ : state)
    {
        (void)_;
        stream.Reset();

        const auto started_at = std::chrono::steady_clock::now();
        pixels.reset(new exr::Rgba[pixel_count]);
        {
            exr::RgbaInputFile input(stream, 1);
            input.setFrameBuffer(pixels.get(), 1, kWidth);
            input.readPixels(0, kHeight - 1);
        }
        const auto finished_at = std::chrono::steady_clock::now();
        state.SetIterationTime(
            std::chrono::duration<double>(finished_at - started_at).count());

        benchmark::DoNotOptimize(pixels.get());
        benchmark::ClobberMemory();
        pixels.reset();
    }

    SetCounters(
        state,
        encoded.size(),
        kCompressionCases[index].has_managed_fixture);
}

void RegisterBenchmarks()
{
    for (size_t index = 0; index < std::size(kCompressionCases); index++)
    {
        const std::string encode_name =
            std::string("OpenEXR/Encode/") + kCompressionCases[index].name;
        const std::string decode_name =
            std::string("OpenEXR/Decode/") + kCompressionCases[index].name;
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
    benchmark::AddCustomContext(
        "OpenEXR",
        std::to_string(OPENEXR_VERSION_MAJOR) + "." +
            std::to_string(OPENEXR_VERSION_MINOR) + "." +
            std::to_string(OPENEXR_VERSION_PATCH));
    benchmark::AddCustomContext(
        "Optimization",
        TINYEXR_BENCHMARK_OPTIMIZATION);
    benchmark::AddCustomContext("Image", "1920x1080 RGBA HALF, single-threaded, memory-only");
    benchmark::AddCustomContext(
        "Timing",
        "result allocation, OpenEXR API, and stream I/O; validation/free excluded");

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
