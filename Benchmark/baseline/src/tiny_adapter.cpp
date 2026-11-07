#include "file_comparison.h"
#include <exr.h>
#include <cstdlib>
#include <cstring>

namespace comparison {
class TinyFile final : public FileAdapter {
    int compression_;
    exr_channel channels_[4]{};
    exr_part part_{};
    exr_image source_{}, decoded_{};
    Planes planes_ = CreateSource();
    void* pointers_[4]{};
    void* encoded_{};
    static void Check(exr_result rc) {
        if (rc != EXR_SUCCESS)
            throw NativeFailure(int(rc), "TinyEXR status " + std::to_string(rc));
    }
public:
    explicit TinyFile(int c) : compression_(c) {
        for (int i = 0; i < 4; ++i) {
            std::memcpy(channels_[i].name, Channels[i], 2);
            channels_[i].pixel_type = EXR_PIXEL_HALF;
            channels_[i].x_sampling = channels_[i].y_sampling = 1;
            pointers_[i] = planes_[i].data();
        }
        part_.width = Width; part_.height = Height; part_.images = pointers_;
        part_.header.part_type = EXR_PART_SCANLINE;
        part_.header.num_channels = 4; part_.header.channels = channels_;
        part_.header.data_window = {0, 0, Width - 1, Height - 1};
        part_.header.display_window = part_.header.data_window;
        part_.header.pixel_aspect_ratio = 1; part_.header.screen_window_width = 1;
        source_.num_parts = 1; source_.parts = &part_;
    }
    ~TinyFile() override { ReleaseResults(); }
    Buffer Encode() override {
        size_t size = 0;
        Check(exr_save_to_memory(&encoded_, &size, nullptr, &source_,
            static_cast<exr_compression>(compression_)));
        return {static_cast<const uint8_t*>(encoded_), size};
    }
    void Decode(const Bytes& bytes) override {
        Check(exr_load_from_memory(bytes.data(), bytes.size(), nullptr, &decoded_));
    }
    const void* PixelData() const override { return decoded_.parts[0].images[0]; }
    Bytes ExportCanonical() const override {
        if (decoded_.num_parts != 1 || decoded_.parts[0].width != Width ||
            decoded_.parts[0].height != Height || decoded_.parts[0].header.num_channels != 4)
            throw std::runtime_error("TinyEXR decoded unexpected geometry");
        const auto& p = decoded_.parts[0];
        std::array<const uint16_t*, 4> planes{};
        for (int c = 0; c < 4; ++c) {
            const auto& channel = p.header.channels[c];
            if (std::strcmp(channel.name, Channels[c]) || channel.pixel_type != EXR_PIXEL_HALF ||
                channel.x_sampling != 1 || channel.y_sampling != 1)
                throw std::runtime_error("TinyEXR decoded unexpected channel");
            planes[c] = static_cast<const uint16_t*>(p.images[c]);
        }
        return Canonical(planes);
    }
    void ReleaseResults() override {
        // A null operation allocator selects malloc/free in the public API.
        std::free(encoded_); encoded_ = nullptr;
        exr_image_free(&decoded_);
    }
};
std::unique_ptr<FileAdapter> MakeTiny(int compression) { return std::make_unique<TinyFile>(compression); }
}
