#include <benchmark/benchmark.h>

#include <tinyexr.h>

#include <cstdint>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <unordered_map>
#include <utility>
#include <vector>

namespace
{

constexpr std::string_view kManifestRelativePath = "Benchmark/sample-manifest.csv";
constexpr std::string_view kOpenExrImagesRelativePath = ".cache/openexr-images";

const std::vector<std::string> kSinglePartSampleIds = {
    "desk_scanline",
    "kapaa_multires",
};

const std::vector<std::string> kMultipartSampleIds = {
    "beachball_multipart_0001",
};

struct SampleEntry
{
    std::string id;
    std::string kind;
    std::string relative_path;
    std::filesystem::path full_path;
    std::vector<unsigned char> bytes;
};

struct ErrorMessage
{
    const char* value = nullptr;

    ~ErrorMessage()
    {
        if (value != nullptr)
        {
            FreeEXRErrorMessage(value);
        }
    }

    const char** Out()
    {
        return &value;
    }
};

[[nodiscard]] std::filesystem::path RepoRoot()
{
    return std::filesystem::path(TINYEXR_BENCHMARK_REPO_ROOT);
}

[[nodiscard]] std::string PathString(const std::filesystem::path& path)
{
    return path.generic_string();
}

[[nodiscard]] std::string JoinError(std::string_view context, const char* error)
{
    std::ostringstream builder;
    builder << context;
    if (error != nullptr && error[0] != '\0')
    {
        builder << ": " << error;
    }

    return builder.str();
}

void ThrowIfFailed(int result, const char* error, std::string_view context)
{
    if (result != TINYEXR_SUCCESS)
    {
        throw std::runtime_error(JoinError(context, error));
    }
}

void ThrowIfSaveFailed(size_t size, const char* error, std::string_view context)
{
    if (size == 0)
    {
        throw std::runtime_error(JoinError(context, error));
    }
}

[[nodiscard]] std::vector<unsigned char> ReadAllBytes(const std::filesystem::path& path)
{
    std::ifstream stream(path, std::ios::binary);
    if (!stream)
    {
        throw std::runtime_error("Unable to open sample file: " + PathString(path));
    }

    stream.seekg(0, std::ios::end);
    const std::streamoff length = stream.tellg();
    stream.seekg(0, std::ios::beg);

    if (length < 0)
    {
        throw std::runtime_error("Unable to determine sample size: " + PathString(path));
    }

    std::vector<unsigned char> bytes(static_cast<size_t>(length));
    if (!bytes.empty())
    {
        stream.read(reinterpret_cast<char*>(bytes.data()), length);
        if (!stream)
        {
            throw std::runtime_error("Unable to read sample file: " + PathString(path));
        }
    }

    return bytes;
}

[[nodiscard]] std::vector<SampleEntry> ParseManifest(const std::filesystem::path& manifest_path)
{
    std::ifstream stream(manifest_path);
    if (!stream)
    {
        throw std::runtime_error("Benchmark sample manifest was not found: " + PathString(manifest_path));
    }

    std::vector<SampleEntry> entries;
    std::string line;
    size_t line_index = 0;
    while (std::getline(stream, line))
    {
        line_index++;
        if (!line.empty() && line.back() == '\r')
        {
            line.pop_back();
        }

        if (line.empty())
        {
            continue;
        }

        if (line_index == 1)
        {
            if (line != "sample_id,kind,relative_path")
            {
                throw std::runtime_error("Unexpected benchmark sample manifest header: " + line);
            }

            continue;
        }

        const size_t first_comma = line.find(',');
        const size_t second_comma = first_comma == std::string::npos ? std::string::npos : line.find(',', first_comma + 1);
        if (first_comma == std::string::npos || second_comma == std::string::npos)
        {
            throw std::runtime_error("Malformed benchmark sample manifest entry at line " + std::to_string(line_index));
        }

        entries.push_back({
            line.substr(0, first_comma),
            line.substr(first_comma + 1, second_comma - first_comma - 1),
            line.substr(second_comma + 1),
            {},
            {},
        });
    }

    return entries;
}

struct RgbaPrepared
{
    std::vector<float> rgba;
    int width = 0;
    int height = 0;
    int components = 4;
    int save_as_fp16 = 0;
};

class SinglePartPrepared
{
public:
    SinglePartPrepared()
    {
        InitEXRHeader(&header_);
        InitEXRImage(&image_);
    }

    ~SinglePartPrepared()
    {
        FreeEXRImage(&image_);
        FreeEXRHeader(&header_);
    }

    SinglePartPrepared(const SinglePartPrepared&) = delete;
    SinglePartPrepared& operator=(const SinglePartPrepared&) = delete;

    EXRVersion version{};
    EXRHeader& header()
    {
        return header_;
    }

    const EXRHeader& header() const
    {
        return header_;
    }

    EXRImage& image()
    {
        return image_;
    }

    const EXRImage& image() const
    {
        return image_;
    }

private:
    EXRHeader header_{};
    EXRImage image_{};
};

class MultipartPrepared
{
public:
    MultipartPrepared() = default;

    ~MultipartPrepared()
    {
        for (EXRImage& image : images_)
        {
            FreeEXRImage(&image);
        }

        for (EXRHeader* header : headers_)
        {
            if (header != nullptr)
            {
                FreeEXRHeader(header);
                std::free(header);
            }
        }
    }

    MultipartPrepared(const MultipartPrepared&) = delete;
    MultipartPrepared& operator=(const MultipartPrepared&) = delete;

    EXRVersion version{};

    std::vector<EXRHeader*>& headers()
    {
        return headers_;
    }

    const std::vector<EXRHeader*>& headers() const
    {
        return headers_;
    }

    std::vector<const EXRHeader*>& header_views()
    {
        return header_views_;
    }

    const std::vector<const EXRHeader*>& header_views() const
    {
        return header_views_;
    }

    std::vector<EXRImage>& images()
    {
        return images_;
    }

    const std::vector<EXRImage>& images() const
    {
        return images_;
    }

private:
    std::vector<EXRHeader*> headers_;
    std::vector<const EXRHeader*> header_views_;
    std::vector<EXRImage> images_;
};

class SampleRepository
{
public:
    static const SampleRepository& Instance()
    {
        static const SampleRepository repository;
        return repository;
    }

    const SampleEntry& Buffer(std::string_view sample_id) const
    {
        return GetEntry(sample_id);
    }

    const RgbaPrepared& Rgba(std::string_view sample_id) const
    {
        return *GetPrepared(rgba_by_id_, sample_id, "RGBA benchmark input");
    }

    const SinglePartPrepared& SinglePart(std::string_view sample_id) const
    {
        return *GetPrepared(single_part_by_id_, sample_id, "single-part benchmark input");
    }

    const MultipartPrepared& Multipart(std::string_view sample_id) const
    {
        return *GetPrepared(multipart_by_id_, sample_id, "multipart benchmark input");
    }

private:
    SampleRepository()
    {
        const std::filesystem::path repo_root = RepoRoot();
        const std::filesystem::path manifest_path = repo_root / kManifestRelativePath;
        const std::filesystem::path cache_root = repo_root / kOpenExrImagesRelativePath;

        if (!std::filesystem::exists(cache_root))
        {
            throw std::runtime_error(
                "OpenEXR sample cache was not found: " + PathString(cache_root));
        }

        std::vector<SampleEntry> manifest_entries = ParseManifest(manifest_path);
        entries_by_id_.reserve(manifest_entries.size());
        for (SampleEntry& entry : manifest_entries)
        {
            std::filesystem::path full_path = cache_root / std::filesystem::path(entry.relative_path);
            if (!std::filesystem::exists(full_path))
            {
                throw std::runtime_error(
                    "Required benchmark sample was not found: " + PathString(full_path));
            }

            entry.full_path = full_path;
            entry.bytes = ReadAllBytes(full_path);
            entries_by_id_.emplace(entry.id, std::move(entry));
        }

        rgba_by_id_.emplace("desk_scanline", PrepareRgba(GetEntry("desk_scanline")));
        single_part_by_id_.emplace("desk_scanline", PrepareSinglePart(GetEntry("desk_scanline")));
        single_part_by_id_.emplace("kapaa_multires", PrepareSinglePart(GetEntry("kapaa_multires")));
        multipart_by_id_.emplace(
            "beachball_multipart_0001",
            PrepareMultipart(GetEntry("beachball_multipart_0001")));
    }

    template <typename TPrepared>
    static const TPrepared* GetPrepared(
        const std::unordered_map<std::string, std::unique_ptr<TPrepared>>& map,
        std::string_view sample_id,
        std::string_view category)
    {
        const auto iterator = map.find(std::string(sample_id));
        if (iterator == map.end())
        {
            throw std::runtime_error(std::string("Missing ") + std::string(category) + " for sample: " + std::string(sample_id));
        }

        return iterator->second.get();
    }

    const SampleEntry& GetEntry(std::string_view sample_id) const
    {
        const auto iterator = entries_by_id_.find(std::string(sample_id));
        if (iterator == entries_by_id_.end())
        {
            throw std::runtime_error("Unknown benchmark sample id: " + std::string(sample_id));
        }

        return iterator->second;
    }

    static std::unique_ptr<RgbaPrepared> PrepareRgba(const SampleEntry& sample)
    {
        auto prepared = std::make_unique<RgbaPrepared>();

        float* rgba = nullptr;
        int width = 0;
        int height = 0;
        ErrorMessage error;
        const int result = LoadEXRFromMemory(
            &rgba,
            &width,
            &height,
            sample.bytes.data(),
            sample.bytes.size(),
            error.Out());
        ThrowIfFailed(result, error.value, std::string("LoadEXRFromMemory setup failed for ") + sample.id);

        std::unique_ptr<float, decltype(&std::free)> rgba_owner(rgba, &std::free);
        prepared->width = width;
        prepared->height = height;
        prepared->rgba.assign(rgba, rgba + static_cast<size_t>(width) * static_cast<size_t>(height) * 4u);
        return prepared;
    }

    static std::unique_ptr<SinglePartPrepared> PrepareSinglePart(const SampleEntry& sample)
    {
        auto prepared = std::make_unique<SinglePartPrepared>();

        const int version_result = ParseEXRVersionFromMemory(
            &prepared->version,
            sample.bytes.data(),
            sample.bytes.size());
        ThrowIfFailed(version_result, nullptr, std::string("ParseEXRVersionFromMemory setup failed for ") + sample.id);

        {
            ErrorMessage error;
            const int header_result = ParseEXRHeaderFromMemory(
                &prepared->header(),
                &prepared->version,
                sample.bytes.data(),
                sample.bytes.size(),
                error.Out());
            ThrowIfFailed(header_result, error.value, std::string("ParseEXRHeaderFromMemory setup failed for ") + sample.id);
        }

        {
            ErrorMessage error;
            const int image_result = LoadEXRImageFromMemory(
                &prepared->image(),
                &prepared->header(),
                sample.bytes.data(),
                sample.bytes.size(),
                error.Out());
            ThrowIfFailed(image_result, error.value, std::string("LoadEXRImageFromMemory setup failed for ") + sample.id);
        }

        return prepared;
    }

    static std::unique_ptr<MultipartPrepared> PrepareMultipart(const SampleEntry& sample)
    {
        auto prepared = std::make_unique<MultipartPrepared>();

        const int version_result = ParseEXRVersionFromMemory(
            &prepared->version,
            sample.bytes.data(),
            sample.bytes.size());
        ThrowIfFailed(version_result, nullptr, std::string("ParseEXRVersionFromMemory setup failed for ") + sample.id);

        EXRHeader** raw_headers = nullptr;
        int header_count = 0;
        {
            ErrorMessage error;
            const int header_result = ParseEXRMultipartHeaderFromMemory(
                &raw_headers,
                &header_count,
                &prepared->version,
                sample.bytes.data(),
                sample.bytes.size(),
                error.Out());
            ThrowIfFailed(header_result, error.value, std::string("ParseEXRMultipartHeaderFromMemory setup failed for ") + sample.id);
        }

        prepared->headers().assign(raw_headers, raw_headers + header_count);
        std::free(raw_headers);

        prepared->header_views().reserve(prepared->headers().size());
        for (EXRHeader* header : prepared->headers())
        {
            prepared->header_views().push_back(header);
        }

        prepared->images().resize(prepared->headers().size());
        for (EXRImage& image : prepared->images())
        {
            InitEXRImage(&image);
        }

        {
            ErrorMessage error;
            const int image_result = LoadEXRMultipartImageFromMemory(
                prepared->images().data(),
                prepared->header_views().data(),
                static_cast<unsigned int>(prepared->headers().size()),
                sample.bytes.data(),
                sample.bytes.size(),
                error.Out());
            ThrowIfFailed(image_result, error.value, std::string("LoadEXRMultipartImageFromMemory setup failed for ") + sample.id);
        }

        return prepared;
    }

    std::unordered_map<std::string, SampleEntry> entries_by_id_;
    std::unordered_map<std::string, std::unique_ptr<RgbaPrepared>> rgba_by_id_;
    std::unordered_map<std::string, std::unique_ptr<SinglePartPrepared>> single_part_by_id_;
    std::unordered_map<std::string, std::unique_ptr<MultipartPrepared>> multipart_by_id_;
};

using SampleBenchmark = void (*)(benchmark::State&, const std::string&);

void RegisterBenchmarksForSamples(
    std::string_view name,
    const std::vector<std::string>& sample_ids,
    SampleBenchmark benchmark_fn)
{
    for (const std::string& sample_id : sample_ids)
    {
        benchmark::RegisterBenchmark(
            (std::string(name) + "/" + sample_id).c_str(),
            [sample_id, benchmark_fn](benchmark::State& state)
            {
                benchmark_fn(state, sample_id);
            });
    }
}

void BM_LoadEXRFromMemory(benchmark::State& state, const std::string& sample_id)
{
    const SampleEntry& sample = SampleRepository::Instance().Buffer(sample_id);
    for (auto _ : state)
    {
        float* rgba = nullptr;
        int width = 0;
        int height = 0;
        ErrorMessage error;
        const int result = LoadEXRFromMemory(
            &rgba,
            &width,
            &height,
            sample.bytes.data(),
            sample.bytes.size(),
            error.Out());
        ThrowIfFailed(result, error.value, std::string("LoadEXRFromMemory benchmark failed for ") + sample_id);

        std::unique_ptr<float, decltype(&std::free)> rgba_owner(rgba, &std::free);
        benchmark::DoNotOptimize(width);
        benchmark::DoNotOptimize(height);
        benchmark::DoNotOptimize(rgba_owner.get());
    }

    state.SetBytesProcessed(static_cast<int64_t>(state.iterations()) * static_cast<int64_t>(sample.bytes.size()));
}

void BM_SaveEXRToMemory(benchmark::State& state, const std::string& sample_id)
{
    const RgbaPrepared& prepared = SampleRepository::Instance().Rgba(sample_id);
    for (auto _ : state)
    {
        unsigned char* encoded = nullptr;
        ErrorMessage error;
        size_t encoded_size = SaveEXRToMemory(
            prepared.rgba.data(),
            prepared.width,
            prepared.height,
            prepared.components,
            prepared.save_as_fp16,
            &encoded,
            error.Out());
        ThrowIfSaveFailed(encoded_size, error.value, std::string("SaveEXRToMemory benchmark failed for ") + sample_id);

        std::unique_ptr<unsigned char, decltype(&std::free)> encoded_owner(encoded, &std::free);
        benchmark::DoNotOptimize(encoded_size);
        benchmark::DoNotOptimize(encoded_owner.get());
    }

    state.SetItemsProcessed(state.iterations());
}

void BM_LoadEXRImageFromMemory(benchmark::State& state, const std::string& sample_id)
{
    const SampleEntry& sample = SampleRepository::Instance().Buffer(sample_id);
    const SinglePartPrepared& prepared = SampleRepository::Instance().SinglePart(sample_id);

    EXRImage image{};
    for (auto _ : state)
    {
        InitEXRImage(&image);
        ErrorMessage error;
        const int result = LoadEXRImageFromMemory(
            &image,
            &prepared.header(),
            sample.bytes.data(),
            sample.bytes.size(),
            error.Out());
        ThrowIfFailed(result, error.value, std::string("LoadEXRImageFromMemory benchmark failed for ") + sample_id);

        benchmark::DoNotOptimize(image.width);
        benchmark::DoNotOptimize(image.height);
        FreeEXRImage(&image);
    }

    state.SetBytesProcessed(static_cast<int64_t>(state.iterations()) * static_cast<int64_t>(sample.bytes.size()));
}

void BM_SaveEXRImageToMemory(benchmark::State& state, const std::string& sample_id)
{
    const SinglePartPrepared& prepared = SampleRepository::Instance().SinglePart(sample_id);
    for (auto _ : state)
    {
        unsigned char* encoded = nullptr;
        ErrorMessage error;
        size_t encoded_size = SaveEXRImageToMemory(
            &prepared.image(),
            &prepared.header(),
            &encoded,
            error.Out());
        ThrowIfSaveFailed(encoded_size, error.value, std::string("SaveEXRImageToMemory benchmark failed for ") + sample_id);

        std::unique_ptr<unsigned char, decltype(&std::free)> encoded_owner(encoded, &std::free);
        benchmark::DoNotOptimize(encoded_size);
        benchmark::DoNotOptimize(encoded_owner.get());
    }

    state.SetItemsProcessed(state.iterations());
}

void BM_LoadEXRMultipartImageFromMemory(benchmark::State& state, const std::string& sample_id)
{
    const SampleEntry& sample = SampleRepository::Instance().Buffer(sample_id);
    const MultipartPrepared& prepared = SampleRepository::Instance().Multipart(sample_id);

    std::vector<EXRImage> images(prepared.headers().size());
    for (auto _ : state)
    {
        for (EXRImage& image : images)
        {
            InitEXRImage(&image);
        }

        ErrorMessage error;
        const int result = LoadEXRMultipartImageFromMemory(
            images.data(),
            const_cast<const EXRHeader**>(prepared.header_views().data()),
            static_cast<unsigned int>(prepared.header_views().size()),
            sample.bytes.data(),
            sample.bytes.size(),
            error.Out());
        ThrowIfFailed(result, error.value, std::string("LoadEXRMultipartImageFromMemory benchmark failed for ") + sample_id);

        benchmark::DoNotOptimize(images.front().width);
        benchmark::DoNotOptimize(images.size());
        for (EXRImage& image : images)
        {
            FreeEXRImage(&image);
        }
    }

    state.SetBytesProcessed(static_cast<int64_t>(state.iterations()) * static_cast<int64_t>(sample.bytes.size()));
}

void BM_SaveEXRMultipartImageToMemory(benchmark::State& state, const std::string& sample_id)
{
    const MultipartPrepared& prepared = SampleRepository::Instance().Multipart(sample_id);
    for (auto _ : state)
    {
        unsigned char* encoded = nullptr;
        ErrorMessage error;
        size_t encoded_size = SaveEXRMultipartImageToMemory(
            prepared.images().data(),
            const_cast<const EXRHeader**>(prepared.header_views().data()),
            static_cast<unsigned int>(prepared.header_views().size()),
            &encoded,
            error.Out());
        ThrowIfSaveFailed(encoded_size, error.value, std::string("SaveEXRMultipartImageToMemory benchmark failed for ") + sample_id);

        std::unique_ptr<unsigned char, decltype(&std::free)> encoded_owner(encoded, &std::free);
        benchmark::DoNotOptimize(encoded_size);
        benchmark::DoNotOptimize(encoded_owner.get());
    }

    state.SetItemsProcessed(state.iterations());
}

void RegisterAllBenchmarks()
{
    RegisterBenchmarksForSamples("LoadEXRFromMemory", std::vector<std::string>{"desk_scanline"}, &BM_LoadEXRFromMemory);
    RegisterBenchmarksForSamples("SaveEXRToMemory", std::vector<std::string>{"desk_scanline"}, &BM_SaveEXRToMemory);
    RegisterBenchmarksForSamples("LoadEXRImageFromMemory", kSinglePartSampleIds, &BM_LoadEXRImageFromMemory);
    RegisterBenchmarksForSamples("SaveEXRImageToMemory", kSinglePartSampleIds, &BM_SaveEXRImageToMemory);
    RegisterBenchmarksForSamples("LoadEXRMultipartImageFromMemory", kMultipartSampleIds, &BM_LoadEXRMultipartImageFromMemory);
    RegisterBenchmarksForSamples("SaveEXRMultipartImageToMemory", kMultipartSampleIds, &BM_SaveEXRMultipartImageToMemory);
}

struct BenchmarkRegistration
{
    BenchmarkRegistration()
    {
        RegisterAllBenchmarks();
    }
} g_benchmark_registration;

}  // namespace

BENCHMARK_MAIN();
