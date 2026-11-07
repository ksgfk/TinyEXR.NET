#include "file_comparison.h"
#include <iostream>
using namespace comparison;
static int Compression(const std::string& name) {
    for (int c = 0; c < 12; ++c) if (name == Name(c)) return c;
    throw std::invalid_argument("Unknown compression: " + name);
}
static auto Adapter(const std::string& name, int c) {
    if (name == "TinyEXR") return MakeTiny(c);
    if (name == "OpenEXR") return MakeOfficial(c);
    throw std::invalid_argument("Unknown implementation");
}
int main(int argc, char** argv) {
    try {
        if (argc == 3 && std::string(argv[1]) == "prepare") {
            std::filesystem::path root(argv[2]);
            std::filesystem::create_directories(root);
            std::ofstream manifest(root / "manifest.csv", std::ios::binary);
            manifest << ManifestHeader << '\n';
            for (int c = 0; c < 12; ++c) {
                auto adapter = MakeOfficial(c);
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
        } else if (argc == 5 && std::string(argv[1]) == "encode") {
            auto adapter = Adapter(argv[2], Compression(argv[3]));
            WriteFile(argv[4], adapter->Encode());
        } else if (argc == 5 && std::string(argv[1]) == "decode") {
            auto input = ReadFile(argv[3]);
            auto adapter = Adapter(argv[2], 0);
            adapter->Decode(input);
            auto raw = adapter->ExportCanonical();
            WriteFile(argv[4], {raw.data(), raw.size()});
        } else throw std::invalid_argument("Usage: prepare DIR | encode IMPLEMENTATION COMPRESSION OUTPUT | decode IMPLEMENTATION INPUT OUTPUT");
        return 0;
    } catch (const NativeFailure& error) {
        std::cerr << (error.code == -3 ? "Unsupported: " : "Error: ") << error.what() << '\n';
        return error.code == -3 ? 3 : 1;
    } catch (const std::exception& error) {
        std::cerr << error.what() << '\n'; return 1;
    }
}
