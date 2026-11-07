#include "file_comparison.h"
#include <ImfChannelList.h>
#include <ImfFrameBuffer.h>
#include <ImfHeader.h>
#include <ImfIO.h>
#include <ImfInputFile.h>
#include <ImfOutputFile.h>
#include <ImfThreading.h>
#include <cstring>
#include <limits>

namespace comparison
{
namespace exr = OPENEXR_IMF_NAMESPACE;
class OutputStream final : public exr::OStream
{
    Bytes bytes_;
    size_t position_ = 0;
public:
    OutputStream() : exr::OStream("<memory>") {}
    void write(const char data[], int n) override
    {
        if (n < 0 || size_t(n) > std::numeric_limits<size_t>::max() - position_) throw std::overflow_error("Output length overflow");
        size_t end = position_ + size_t(n);
        if (end > bytes_.size()) bytes_.resize(end);
        std::memcpy(bytes_.data() + position_, data, size_t(n)); position_ = end;
    }
    uint64_t tellp() override { return position_; }
    void seekp(uint64_t offset) override
    {
        if (offset > std::numeric_limits<size_t>::max()) throw std::overflow_error("Output seek overflow");
        position_ = size_t(offset);
    }
    Bytes Take() { return std::move(bytes_); }
};
class InputStream final : public exr::IStream
{
    const Bytes& bytes_; size_t position_ = 0;
    size_t Range(int n)
    {
        if (n < 0 || size_t(n) > bytes_.size() - position_) throw std::runtime_error("Truncated EXR input");
        size_t start = position_; position_ += size_t(n); return start;
    }
public:
    explicit InputStream(const Bytes& bytes) : exr::IStream("<memory>"), bytes_(bytes) {}
    bool isMemoryMapped() const override { return true; }
    char* readMemoryMapped(int n) override { return reinterpret_cast<char*>(const_cast<uint8_t*>(bytes_.data() + Range(n))); }
    bool read(char output[], int n) override { size_t start = Range(n); std::memcpy(output, bytes_.data() + start, size_t(n)); return position_ < bytes_.size(); }
    uint64_t tellg() override { return position_; }
    void seekg(uint64_t offset) override { if (offset > bytes_.size()) throw std::runtime_error("Input seek out of range"); position_ = size_t(offset); }
    int64_t size() override { return int64_t(bytes_.size()); }
};

class OfficialFile final : public FileAdapter
{
    exr::Header header_;
    Planes source_ = CreateSource(), decoded_;
    exr::FrameBuffer sourceFrame_;
    Bytes encoded_;
    std::unique_ptr<exr::Header> decodedHeader_;
    static exr::FrameBuffer FrameBuffer(Planes& planes)
    {
        exr::FrameBuffer result;
        for (int c = 0; c < 4; ++c)
            result.insert(Channels[c], exr::Slice(exr::HALF, reinterpret_cast<char*>(planes[c].data()), sizeof(uint16_t), Width * sizeof(uint16_t)));
        return result;
    }
public:
    explicit OfficialFile(int compression) : header_(Width, Height)
    {
        exr::setGlobalThreadCount(0);
        header_.compression() = static_cast<exr::Compression>(compression);
        for (const char* name : Channels) header_.channels().insert(name, exr::Channel(exr::HALF));
        sourceFrame_ = FrameBuffer(source_);
    }
    Buffer Encode() override
    {
        OutputStream stream;
        {
            exr::OutputFile file(stream, header_, 0);
            file.setFrameBuffer(sourceFrame_); file.writePixels(Height);
        }
        encoded_ = stream.Take();
        return {encoded_.data(), encoded_.size()};
    }
    void Decode(const Bytes& bytes) override
    {
        InputStream stream(bytes); exr::InputFile file(stream, 0);
        const auto& window = file.header().dataWindow();
        if (window.min.x != 0 || window.min.y != 0 || window.max.x != Width - 1 || window.max.y != Height - 1)
            throw std::runtime_error("Unexpected decoded image dimensions");
        decodedHeader_ = std::make_unique<exr::Header>(file.header());
        for (int c = 0; c < 4; ++c)
        {
            const auto* channel = file.header().channels().findChannel(Channels[c]);
            if (!channel || channel->type != exr::HALF || channel->xSampling != 1 || channel->ySampling != 1)
                throw std::runtime_error("Unexpected decoded channel layout");
            decoded_[c].resize(size_t(Width) * Height);
        }
        file.setFrameBuffer(FrameBuffer(decoded_)); file.readPixels(0, Height - 1);
    }
    const void* PixelData() const override { return decoded_[0].data(); }
    Bytes ExportCanonical() const override
    {
        return Canonical({decoded_[0].data(), decoded_[1].data(), decoded_[2].data(), decoded_[3].data()});
    }
    void ReleaseResults() override
    {
        Bytes().swap(encoded_);
        for (auto& plane : decoded_) std::vector<uint16_t>().swap(plane);
        decodedHeader_.reset();
    }
};
std::unique_ptr<FileAdapter> MakeOfficial(int c) { return std::make_unique<OfficialFile>(c); }
}
