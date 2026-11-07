// Test-only full-image adapter. All parsing, validation and coding is performed
// by the unmodified official OpenEXR library, including every deep/tiled level.
#include <ImfMultiPartInputFile.h>
#include <ImfMultiPartOutputFile.h>
#include <ImfInputPart.h>
#include <ImfOutputPart.h>
#include <ImfTiledInputPart.h>
#include <ImfTiledOutputPart.h>
#include <ImfDeepScanLineInputPart.h>
#include <ImfDeepScanLineOutputPart.h>
#include <ImfDeepTiledInputPart.h>
#include <ImfDeepTiledOutputPart.h>
#include <ImfDeepFrameBuffer.h>
#include <ImfHeader.h>
#include <ImfChannelList.h>
#include <ImfPartType.h>
#include <ImfIO.h>
#include <fstream>
#include <iostream>
#include <vector>
#include <memory>
#include <cstdint>
#include <numeric>
#include <cstring>
using namespace OPENEXR_IMF_NAMESPACE;
using namespace IMATH_NAMESPACE;
extern "C" {
    extern uint16_t* exrcore_expTable;
    extern uint16_t* exrcore_logTable;
    void exrcore_ensure_b44_tables();
}

template<class T> struct Grid
{
    std::vector<T> storage;
    size_t start = 0, baseOffset = 0, count = 0;
    Grid(int x, int y, int w, int h)
    {
        int64_t offset = -int64_t(x) - int64_t(y) * w;
        start = size_t(std::max<int64_t>(0, -offset));
        baseOffset = size_t(int64_t(start) + offset);
        count = size_t(w) * h;
        storage.resize(std::max(start + count, baseOffset + 1));
    }
    T* data() { return storage.data() + start; }
    char* base() { return reinterpret_cast<char*>(storage.data() + baseOffset); }
};
struct Plane
{
    std::string name;
    Channel channel;
    size_t bytes;
    std::unique_ptr<Grid<char>> flat;
    std::unique_ptr<Grid<char*>> pointers;
    std::vector<char> samples;
    Plane(std::string n, Channel c) : name(n), channel(c), bytes(c.type == HALF ? 2 : 4) {}
};
struct Level
{
    int lx, ly, w, h;
    Box2i region;
    std::unique_ptr<Grid<uint32_t>> counts;
    std::vector<Plane> planes;
    FrameBuffer flatBuffer;
    DeepFrameBuffer deepBuffer;
    Level(const Header& header, Box2i r, int x, int y) : lx(x), ly(y), w(r.max.x-r.min.x+1), h(r.max.y-r.min.y+1), region(r)
    {
        bool deep = isDeepData(header.type());
        if (deep)
        {
            counts = std::make_unique<Grid<uint32_t>>(r.min.x, r.min.y, w, h);
            deepBuffer.insertSampleCountSlice(Slice(UINT, counts->base(), 4, size_t(w)*4));
        }
        for (auto c = header.channels().begin(); c != header.channels().end(); ++c)
        {
            planes.emplace_back(c.name(), c.channel());
            Plane& p = planes.back();
            if (deep)
            {
                p.pointers = std::make_unique<Grid<char*>>(r.min.x, r.min.y, w, h);
                deepBuffer.insert(p.name, DeepSlice(p.channel.type, p.pointers->base(), sizeof(char*), size_t(w)*sizeof(char*), p.bytes));
            }
            else
            {
                int sw = w/p.channel.xSampling, sh = h/p.channel.ySampling;
                p.flat = std::make_unique<Grid<char>>(r.min.x/p.channel.xSampling * int(p.bytes), r.min.y/p.channel.ySampling, sw*int(p.bytes), sh);
                flatBuffer.insert(p.name, Slice(p.channel.type, p.flat->base(), p.bytes, size_t(sw)*p.bytes, p.channel.xSampling, p.channel.ySampling));
            }
        }
    }
    void allocateSamples()
    {
        size_t total = std::accumulate(counts->data(), counts->data()+counts->count, size_t(0));
        for (auto& p : planes)
        {
            p.samples.resize(total*p.bytes);
            size_t offset = 0;
            for (size_t i=0; i<counts->count; ++i)
            {
                p.pointers->data()[i] = counts->data()[i] ? p.samples.data()+offset : nullptr;
                offset += counts->data()[i]*p.bytes;
            }
        }
    }
};
template<class P> std::vector<std::unique_ptr<Level>> readTiles(P& part, const Header& header)
{
    std::vector<std::unique_ptr<Level>> result;
    for (int ly=0; ly<part.numYLevels(); ++ly)
        for (int lx=0; lx<part.numXLevels(); ++lx)
        {
            if (!part.isValidLevel(lx,ly)) continue;
            auto level = std::make_unique<Level>(header, part.dataWindowForLevel(lx,ly), lx,ly);
            if constexpr (std::is_same_v<P,DeepTiledInputPart>)
            {
                part.setFrameBuffer(level->deepBuffer);
                part.readPixelSampleCounts(0,part.numXTiles(lx)-1,0,part.numYTiles(ly)-1,lx,ly);
                level->allocateSamples();
                part.readTiles(0,part.numXTiles(lx)-1,0,part.numYTiles(ly)-1,lx,ly);
            }
            else
            {
                part.setFrameBuffer(level->flatBuffer);
                part.readTiles(0,part.numXTiles(lx)-1,0,part.numYTiles(ly)-1,lx,ly);
            }
            result.push_back(std::move(level));
        }
    return result;
}
std::vector<std::unique_ptr<Level>> readPart(MultiPartInputFile& input, int index)
{
    const Header& header = input.header(index);
    if (header.type() == TILEDIMAGE) { TiledInputPart p(input,index); return readTiles(p,header); }
    if (header.type() == DEEPTILE) { DeepTiledInputPart p(input,index); return readTiles(p,header); }
    auto level = std::make_unique<Level>(header, header.dataWindow(),0,0);
    if (header.type() == DEEPSCANLINE)
    {
        DeepScanLineInputPart p(input,index);
        p.setFrameBuffer(level->deepBuffer);
        p.readPixelSampleCounts(level->region.min.y,level->region.max.y);
        level->allocateSamples();
        p.readPixels(level->region.min.y,level->region.max.y);
    }
    else
    {
        InputPart p(input,index); p.setFrameBuffer(level->flatBuffer);
        p.readPixels(level->region.min.y,level->region.max.y);
    }
    std::vector<std::unique_ptr<Level>> result; result.push_back(std::move(level)); return result;
}
template<class T> void put(std::ostream& output, T value)
{
    // The canonical oracle protocol is explicitly little-endian.
    for (size_t i=0; i<sizeof(T); ++i) output.put(char((uint64_t(value) >> (i*8)) & 255));
}
struct AttributeStream : OStream
{
    std::vector<char> bytes;
    uint64_t position=0;
    AttributeStream() : OStream("attribute") {}
    void write(const char* source, int size) override
    {
        if (size<0 || position+uint64_t(size)>SIZE_MAX) throw std::runtime_error("attribute size");
        bytes.resize(std::max(bytes.size(),size_t(position)+size));
        std::memcpy(bytes.data()+position,source,size); position+=size;
    }
    uint64_t tellp() override { return position; }
    void seekp(uint64_t offset) override { position=offset; }
};
bool dumpAttribute(const std::string& name)
{
    // C++ synthesizes type/chunkCount for old single-part files. Deep summary
    // fields can also be recomputed; compare actual samples/counts separately.
    return name!="channels" && name!="type" && name!="chunkCount" && name!="version" && name!="maxSamplesPerPixel";
}
void putString(std::ostream& output, const std::string& value)
{
    put<int32_t>(output,int(value.size())); output.write(value.data(),value.size());
}
void dump(std::ostream& output, const Header& header, const std::vector<std::unique_ptr<Level>>& levels)
{
    put<int32_t>(output, header.type()==SCANLINEIMAGE ? 0 : header.type()==TILEDIMAGE ? 1 : header.type()==DEEPSCANLINE ? 2 : 3);
    int attributes=0;
    for (auto a=header.begin();a!=header.end();++a) if (dumpAttribute(a.name())) ++attributes;
    put<int32_t>(output,attributes);
    for (auto a=header.begin();a!=header.end();++a)
    {
        if (!dumpAttribute(a.name())) continue;
        putString(output,a.name()); putString(output,a.attribute().typeName());
        AttributeStream value; a.attribute().writeValueTo(value,2);
        put<int32_t>(output,int(value.bytes.size())); output.write(value.bytes.data(),value.bytes.size());
    }
    put<int32_t>(output, int(levels[0]->planes.size()));
    for (const auto& p : levels[0]->planes)
    {
        put<int32_t>(output,int(p.name.size())); output.write(p.name.data(),p.name.size());
        put<int32_t>(output,p.channel.type); put<int32_t>(output,p.channel.xSampling); put<int32_t>(output,p.channel.ySampling); put<int32_t>(output,p.channel.pLinear ? 1 : 0);
    }
    put<int32_t>(output,int(levels.size()));
    for (const auto& l : levels)
    {
        put<int32_t>(output,l->lx); put<int32_t>(output,l->ly);
        put<int32_t>(output,l->region.min.x); put<int32_t>(output,l->region.min.y);
        put<int32_t>(output,l->w); put<int32_t>(output,l->h); put<int32_t>(output,l->counts ? 1 : 0);
        if (l->counts)
        {
            put<int32_t>(output,int(l->counts->count));
            for (size_t i=0; i<l->counts->count; ++i) put<uint32_t>(output,l->counts->data()[i]);
        }
        for (const auto& p : l->planes)
        {
            size_t size = l->counts ? p.samples.size() : p.flat->count;
            put<uint64_t>(output,size);
            output.write(l->counts ? p.samples.data() : p.flat->data(),size);
        }
    }
}
void writePart(MultiPartOutputFile& output, int index, const Header& header, std::vector<std::unique_ptr<Level>>& levels)
{
    if (header.type()==TILEDIMAGE)
    {
        TiledOutputPart p(output,index);
        for (auto& l : levels) { p.setFrameBuffer(l->flatBuffer); p.writeTiles(0,p.numXTiles(l->lx)-1,0,p.numYTiles(l->ly)-1,l->lx,l->ly); }
    }
    else if (header.type()==DEEPTILE)
    {
        DeepTiledOutputPart p(output,index);
        for (auto& l : levels) { p.setFrameBuffer(l->deepBuffer); p.writeTiles(0,p.numXTiles(l->lx)-1,0,p.numYTiles(l->ly)-1,l->lx,l->ly); }
    }
    else if (header.type()==DEEPSCANLINE)
    {
        DeepScanLineOutputPart p(output,index); p.setFrameBuffer(levels[0]->deepBuffer); p.writePixels(levels[0]->h);
    }
    else { OutputPart p(output,index); p.setFrameBuffer(levels[0]->flatBuffer); p.writePixels(levels[0]->h); }
}
// Generate independently of the managed writer: mixed channels, negative
// windows, sampled scanlines, edge tiles, ripmaps and variable/zero deep counts.
void fillLevel(Level& level)
{
    if (level.counts)
    {
        for (size_t i=0;i<level.counts->count;++i) level.counts->data()[i]=uint32_t(i%4);
        level.allocateSamples();
    }
    const uint32_t floats[] = {0,0x80000000,1,0x007fffff,0x00800000,0x7f7fffff,0xff7fffff,0x7f800000,0xff800000,0x7fc12345,0x7f800001,0x3f800000};
    const uint16_t halves[] = {0,0x8000,1,0x3ff,0x400,0x7bff,0xfbff,0x7c00,0xfc00,0x7e01,0x7c01,0x3c00};
    for (auto& plane : level.planes)
    {
        char* data=level.counts ? plane.samples.data() : plane.flat->data();
        size_t bytes=level.counts ? plane.samples.size() : plane.flat->count;
        for (size_t i=0;i<bytes/plane.bytes;++i)
        {
            uint32_t value=plane.channel.type==UINT ? uint32_t(i)*2654435761U : plane.channel.type==HALF ? halves[i%12] : floats[i%12];
            std::memcpy(data+i*plane.bytes,&value,plane.bytes);
        }
    }
}
template<class P> std::vector<std::unique_ptr<Level>> generateTiles(P& part, const Header& header)
{
    std::vector<std::unique_ptr<Level>> levels;
    for (int ly=0;ly<part.numYLevels();++ly) for (int lx=0;lx<part.numXLevels();++lx)
    {
        if (!part.isValidLevel(lx,ly)) continue;
        auto level=std::make_unique<Level>(header,part.dataWindowForLevel(lx,ly),lx,ly);
        fillLevel(*level); levels.push_back(std::move(level));
    }
    return levels;
}
void generate(const char* path, Compression compression)
{
    std::vector<Header> headers;
    int parts=(compression==NO_COMPRESSION || compression==RLE_COMPRESSION || compression==ZIPS_COMPRESSION) ? 4 : 2;
    for (int p=0;p<parts;++p)
    {
        bool tiled=(p%2)!=0, deep=p>=2;
        Box2i display(V2i(-4,-2),V2i(123,29));
        Header h(display,Box2i(V2i(-4,-2),tiled ? V2i(12,6) : V2i(123,29)));
        h.compression()=compression; h.setName("generated"+std::to_string(p));
        h.setType(deep ? (tiled ? DEEPTILE : DEEPSCANLINE) : (tiled ? TILEDIMAGE : SCANLINEIMAGE));
        h.channels().insert("F",Channel(FLOAT));
        h.channels().insert("H",Channel(HALF,(!tiled && !deep) ? 2 : 1,(!tiled && !deep) ? 2 : 1,true));
        h.channels().insert("U",Channel(UINT));
        if (tiled) h.setTileDescription(TileDescription(8,4,RIPMAP_LEVELS,ROUND_UP));
        headers.push_back(h);
    }
    MultiPartOutputFile output(path,headers.data(),int(headers.size()));
    for (int p=0;p<parts;++p)
    {
        std::vector<std::unique_ptr<Level>> levels;
        if (headers[p].type()==TILEDIMAGE) { TiledOutputPart part(output,p); levels=generateTiles(part,headers[p]); }
        else if (headers[p].type()==DEEPTILE) { DeepTiledOutputPart part(output,p); levels=generateTiles(part,headers[p]); }
        else { auto level=std::make_unique<Level>(headers[p],headers[p].dataWindow(),0,0); fillLevel(*level); levels.push_back(std::move(level)); }
        writePart(output,p,headers[p],levels);
    }
}
int main(int argc, char** argv)
{
    try
    {
        if (argc==3 && std::string(argv[1])=="tables")
        {
            exrcore_ensure_b44_tables(); std::ofstream output(argv[2],std::ios::binary);
            for (auto table : {exrcore_expTable,exrcore_logTable}) for (int i=0;i<65536;++i) put<uint16_t>(output,table[i]);
            if (!output) throw std::runtime_error("tables I/O"); return 0;
        }
        if (argc!=4) throw std::runtime_error("dump|reencode input output");
        if (std::string(argv[1])=="generate") { generate(argv[3],Compression(std::stoi(argv[2]))); return 0; }
        MultiPartInputFile input(argv[2]);
        std::vector<Header> headers;
        for (int p=0;p<input.parts();++p) { headers.push_back(input.header(p)); if (!headers.back().hasName()) headers.back().setName("part"+std::to_string(p)); }
        if (std::string(argv[1])=="dump")
        {
            std::ofstream output(argv[3],std::ios::binary); put<int32_t>(output,input.parts());
            for (int p=0;p<input.parts();++p) dump(output,input.header(p),readPart(input,p));
            if (!output) throw std::runtime_error("dump I/O");
        }
        else if (std::string(argv[1])=="reencode")
        {
            MultiPartOutputFile output(argv[3],headers.data(),int(headers.size()));
            for (int p=0;p<input.parts();++p) { auto levels = readPart(input,p); writePart(output,p,headers[p],levels); }
        }
        else throw std::runtime_error("unknown mode");
        return 0;
    }
    catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
