#pragma once
#include <algorithm>
#include <array>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iterator>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace comparison
{
constexpr int Width = 1920, Height = 1080, ChannelCount = 4;
constexpr int64_t RawBytes = int64_t(Width) * Height * ChannelCount * 2;
constexpr const char* OpenExrCommit = "3950290530c4fd4776667d0dc4d65a9d003eb936";
inline constexpr const char* Names[] = {"None", "RLE", "ZIPS", "ZIP", "PIZ", "PXR24", "B44", "B44A", "DWAA", "DWAB", "HTJ2K256", "HTJ2K32"};
inline constexpr const char* Channels[] = {"A", "B", "G", "R"};
inline constexpr const char* ManifestHeader = "Compression,File,EncodedBytes,Fnv1a64,RawFile,RawBytes,RawFnv1a64,OpenEXRCommit,Width,Height,Layout";
using Bytes = std::vector<uint8_t>;
using Planes = std::array<std::vector<uint16_t>, 4>;
struct Buffer { const uint8_t* data; size_t size; };

class NativeFailure : public std::runtime_error
{
public:
    int code;
    NativeFailure(int value, const std::string& message) : std::runtime_error(message), code(value) {}
};

inline const char* Name(int c)
{
    if (c < 0 || c >= 12) throw std::invalid_argument("Compression is outside the benchmark matrix");
    return Names[c];
}
inline bool Shared(int c) { return c != 8 && c != 9; }
inline std::filesystem::path FixtureRoot()
{
    return std::filesystem::path(TINYEXR_BENCHMARK_REPO_ROOT) / ".cache/compression-benchmarks/official";
}
inline uint16_t Sample(int c, int x, int y)
{
    switch (c)
    {
        case 0: return 0x3c00;
        case 1: return uint16_t(0x3000 | (((x >> 2) + 3 * (y >> 2)) & 1023));
        case 2: return uint16_t(0x3400 | (y * 1023 / (Height - 1)));
        default: return uint16_t(0x3800 | (x * 1023 / (Width - 1)));
    }
}
inline Planes CreateSource()
{
    Planes planes;
    for (int c = 0; c < 4; ++c)
    {
        planes[c].resize(size_t(Width) * Height);
        for (int y = 0; y < Height; ++y)
            for (int x = 0; x < Width; ++x) planes[c][size_t(y) * Width + x] = Sample(c, x, y);
    }
    return planes;
}
inline Bytes Canonical(const std::array<const uint16_t*, 4>& planes)
{
    Bytes bytes; bytes.reserve(size_t(RawBytes));
    for (int y = 0; y < Height; ++y)
        for (int c = 0; c < 4; ++c)
            for (int x = 0; x < Width; ++x)
            {
                uint16_t v = planes[c][size_t(y) * Width + x];
                bytes.push_back(uint8_t(v)); bytes.push_back(uint8_t(v >> 8));
            }
    return bytes;
}
inline Bytes ReadFile(const std::filesystem::path& path)
{
    std::ifstream input(path, std::ios::binary);
    if (!input) throw std::runtime_error("Missing fixture: " + path.string());
    return Bytes(std::istreambuf_iterator<char>(input), {});
}
inline void WriteFile(const std::filesystem::path& path, Buffer bytes)
{
    std::ofstream output(path, std::ios::binary);
    output.write(reinterpret_cast<const char*>(bytes.data), std::streamsize(bytes.size));
    if (!output) throw std::runtime_error("Unable to write " + path.string());
}
// Integrity check for prepared artifacts; not a cryptographic signature.
inline std::string Checksum(Buffer bytes)
{
    uint64_t h = 14695981039346656037ULL;
    for (size_t i = 0; i < bytes.size; ++i) { h ^= bytes.data[i]; h *= 1099511628211ULL; }
    std::ostringstream text; text << std::hex << std::setfill('0') << std::setw(16) << h;
    return text.str();
}
inline Bytes LoadVerified(int c)
{
    std::ifstream manifest(FixtureRoot() / "manifest.csv"); std::string line;
    if (!std::getline(manifest, line) || line != ManifestHeader) throw std::runtime_error("Prepare official file fixtures first");
    bool found = false; std::array<bool, 12> seen{}; Bytes bytes;
    while (std::getline(manifest, line))
    {
        std::istringstream row(line); std::vector<std::string> cells; std::string cell;
        while (std::getline(row, cell, ',')) cells.push_back(cell);
        if (cells.size() != 11) throw std::runtime_error("Invalid fixture manifest row");
        auto position = std::find(std::begin(Names), std::end(Names), cells[0]);
        if (position == std::end(Names) || seen[size_t(position - std::begin(Names))]) throw std::runtime_error("Duplicate/unknown fixture");
        seen[size_t(position - std::begin(Names))] = true;
        if (cells[7] != OpenExrCommit || cells[8] != "1920" || cells[9] != "1080" || cells[10] != "ABGR_HALF")
            throw std::runtime_error("Fixture version/geometry mismatch");
        if (cells[0] != Name(c)) continue;
        if (cells[1] != std::string(Name(c)) + ".exr") throw std::runtime_error("Invalid fixture path");
        bytes = ReadFile(FixtureRoot() / cells[1]);
        if (std::stoull(cells[2]) != bytes.size() || cells[3] != Checksum({bytes.data(), bytes.size()}))
            throw std::runtime_error("Fixture checksum mismatch");
        found = true;
    }
    if (!found || std::find(seen.begin(), seen.end(), false) != seen.end()) throw std::runtime_error("Incomplete fixture manifest");
    return bytes;
}

class FileAdapter
{
public:
    virtual ~FileAdapter() = default;
    virtual Buffer Encode() = 0;
    virtual void Decode(const Bytes& input) = 0;
    virtual const void* PixelData() const = 0;
    virtual Bytes ExportCanonical() const = 0;
    virtual void ReleaseResults() = 0;
};
std::unique_ptr<FileAdapter> MakeOfficial(int compression);
std::unique_ptr<FileAdapter> MakeTiny(int compression);
}
