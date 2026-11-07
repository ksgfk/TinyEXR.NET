#include "file_comparison.h"
#include <iostream>
using namespace comparison;
#ifdef USE_TINYEXR
constexpr const char* Implementation = "TinyEXR";
static auto Adapter(int c) { return MakeTiny(c); }
#else
constexpr const char* Implementation = "OpenEXR";
static auto Adapter(int c) { return MakeOfficial(c); }
#endif
static int Compression(const std::string& name) {
    for (int c = 0; c < 12; ++c) if (name == Name(c)) return c;
    throw std::invalid_argument("Unknown compression: " + name);
}
int main(int argc, char** argv) {
    try {
        if (argc == 2 && std::string(argv[1]) == "--implementation") {
            std::cout << Implementation << '\n';
            return 0;
        }
#ifndef USE_TINYEXR
        // Only the official tool can prepare the shared correctness fixtures.
        if (argc == 3 && std::string(argv[1]) == "prepare") {
            std::filesystem::path root(argv[2]);
            std::filesystem::create_directories(root);
            std::ofstream manifest(root / "manifest.csv", std::ios::binary);
            manifest << ManifestHeader << '\n';
            for (int c = 0; c < 12; ++c) {
                auto adapter = Adapter(c);
                auto encoded = adapter->Encode();
                Bytes input(encoded.data, encoded.data + encoded.size);
                WriteFile(root / (std::string(Name(c)) + ".exr"), encoded);
                adapter->ReleaseResults();
                adapter->Decode(input);
                auto raw = adapter->ExportCanonical();
                WriteFile(root / (std::string(Name(c)) + ".raw"), {raw.data(), raw.size()});
                manifest << Name(c) << ',' << Name(c) << ".exr," << input.size() << ','
                    << Checksum({input.data(), input.size()}) << ',' << Name(c) << ".raw,"
                    << raw.size() << ',' << Checksum({raw.data(), raw.size()}) << ','
                    << OpenExrCommit << ',' << Width << ',' << Height << ",ABGR_HALF\n";
            }
            if (!manifest) throw std::runtime_error("Writing manifest failed");
        } else
#endif
        if (argc == 4 && std::string(argv[1]) == "encode") {
            auto adapter = Adapter(Compression(argv[2]));
            WriteFile(argv[3], adapter->Encode());
        } else if (argc == 4 && std::string(argv[1]) == "decode") {
            auto input = ReadFile(argv[2]);
            auto adapter = Adapter(0);
            adapter->Decode(input);
            auto raw = adapter->ExportCanonical();
            WriteFile(argv[3], {raw.data(), raw.size()});
        } else throw std::invalid_argument(std::string(Implementation) +
            " usage: --implementation | encode COMPRESSION OUTPUT | decode INPUT OUTPUT"
#ifndef USE_TINYEXR
            " | prepare DIR"
#endif
        );
        return 0;
    } catch (const NativeFailure& error) {
        std::cerr << (error.code == -3 ? "Unsupported: " : "Error: ") << error.what() << '\n';
        return error.code == -3 ? 3 : 1;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n'; return 1;
    }
}
