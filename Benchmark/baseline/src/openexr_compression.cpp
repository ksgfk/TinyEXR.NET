#include "file_comparison.h"
#include <benchmark/benchmark.h>
#include <iostream>
using namespace comparison;
#ifdef USE_TINYEXR
constexpr const char* Implementation = "TinyEXR";
static auto Make(int c) { return MakeTiny(c); }
#else
constexpr const char* Implementation = "OpenEXR";
static auto Make(int c) { return MakeOfficial(c); }
#endif

static void Measure(benchmark::State& state, int compression, bool encode) {
    try {
        // Source construction, fixture I/O, integrity checks and warmup are untimed.
        auto adapter = Make(compression);
        auto input = LoadVerified(compression);
        size_t encodedSize = input.size();
        if (encode) encodedSize = adapter->Encode().size;
        else adapter->Decode(input);
        adapter->ReleaseResults();
        for (auto _ : state) {
            if (encode) {
                auto output = adapter->Encode();
                benchmark::DoNotOptimize(output.data);
                benchmark::DoNotOptimize(output.size);
            } else {
                adapter->Decode(input);
                benchmark::DoNotOptimize(adapter->PixelData());
            }
            benchmark::ClobberMemory();
            state.PauseTiming();
            adapter->ReleaseResults();
            state.ResumeTiming();
        }
        state.SetBytesProcessed(state.iterations() * RawBytes);
        state.counters["EncodedBytes"] = double(encodedSize);
        state.counters["RawBytes"] = double(RawBytes);
        state.counters["Ratio"] = double(RawBytes) / encodedSize;
    } catch (const NativeFailure& error) {
        if (std::string(Implementation) == "TinyEXR" && !encode &&
            compression >= 10 && error.code == -3)
            state.SkipWithError("Unsupported: TinyEXR EXR_ERROR_UNSUPPORTED (-3); official HTJ2K multiple tile-parts");
        else state.SkipWithError(error.what());
    } catch (const std::exception& error) {
        state.SkipWithError(error.what());
    }
}
int main(int argc, char** argv) {
    benchmark::Initialize(&argc, argv);
    if (benchmark::ReportUnrecognizedArguments(argc, argv)) return 1;
    benchmark::AddCustomContext("scope", "whole-image memory API; fresh read/write state and result storage");
    benchmark::AddCustomContext("compiler", TINYEXR_BENCHMARK_COMPILER);
    benchmark::AddCustomContext("optimization", TINYEXR_BENCHMARK_OPTIMIZATION);
    benchmark::AddCustomContext("threads", "1");
    benchmark::AddCustomContext("openexr_commit", OpenExrCommit);
    benchmark::AddCustomContext("tinyexr_commit", TINYEXR_SOURCE_COMMIT);
    benchmark::AddCustomContext("settings", std::string(Implementation) == "TinyEXR" ?
        "vendored libdeflate 1.25; default level 4; no persistent context" :
        "OpenEXR 3.5.3 defaults; ZIP level 4; DWA level 45; pinned libdeflate; no persistent context");
    for (int c = 0; c < 12; ++c) {
        if (std::string(Implementation) == "TinyEXR" && !Shared(c)) continue;
        for (bool encode : {true, false}) {
            auto name = std::string(Implementation) + "/" + (encode ? "Encode/" : "Decode/") + Name(c);
            benchmark::RegisterBenchmark(name.c_str(), [c, encode](benchmark::State& s) { Measure(s, c, encode); })
                ->Unit(benchmark::kMillisecond)->UseRealTime();
        }
    }
    benchmark::RunSpecifiedBenchmarks();
    benchmark::Shutdown();
}
