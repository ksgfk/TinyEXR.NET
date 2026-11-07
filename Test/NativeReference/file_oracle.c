/* Test-only block oracle. Uses the pinned V3 API for every level and deep sample.
 * No base-level-only image comparison or legacy decoder is involved. */
#include "exr.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

extern void exr_b44_debug_tables(const uint16_t **exp_table, const uint16_t **log_table);

#define CHECK(call) do { exr_result rc_ = (call); if (rc_ != EXR_SUCCESS) { \
    fprintf(stderr, "%s: %s\n", #call, exr_result_string(rc_)); exit(1); } } while (0)

static void bytes(FILE *f, const void *p, size_t n) {
    if (n && fwrite(p, 1, n, f) != n) { perror("write"); exit(2); }
}
static void u32(FILE *f, uint32_t v) {
    unsigned char b[4];
    for (int i = 0; i < 4; ++i) b[i] = (unsigned char)(v >> (i * 8));
    bytes(f, b, 4);
}
static void u64(FILE *f, uint64_t v) { u32(f, (uint32_t)v); u32(f, (uint32_t)(v >> 32)); }
static void blob(FILE *f, const void *p, size_t n) { u64(f, n); bytes(f, p, n); }
static void text(FILE *f, const char *s) { u32(f, (uint32_t)strlen(s)); bytes(f, s, strlen(s)); }
static void *allocate(size_t n) { void *p = calloc(1, n ? n : 1); if (!p) exit(2); return p; }
static int64_t floor_div(int64_t a, int64_t b) { int64_t q = a / b; return q - (a % b < 0); }
static size_t samples(int lo, int size, int step) {
    return (size_t)(floor_div((int64_t)lo + size - 1, step) - floor_div((int64_t)lo - 1, step));
}

int main(int argc, char **argv) {
    if (argc == 3 && strcmp(argv[1], "tables") == 0) {
        const uint16_t *exp_table, *log_table;
        exr_b44_debug_tables(&exp_table, &log_table);
        FILE *f = fopen(argv[2], "wb"); if (!f) return 2;
        for (int t = 0; t < 2; t++) for (int i = 0; i < 65536; i++) {
            uint16_t v = t ? log_table[i] : exp_table[i];
            unsigned char b[2] = { (unsigned char)v, (unsigned char)(v >> 8) }; bytes(f, b, 2);
        }
        return fclose(f) != 0;
    }
    if (argc != 4 || (strcmp(argv[1], "dump") && strcmp(argv[1], "reencode"))) {
        fprintf(stderr, "file_oracle dump|reencode input.exr output; or tables output\n"); return 2;
    }
    int encode = strcmp(argv[1], "reencode") == 0;
    exr_reader *r = NULL; exr_writer *w = NULL; FILE *f = NULL;
    CHECK(exr_reader_open_file(argv[2], NULL, &r)); CHECK(exr_reader_parse_header(r));
    int nparts = exr_reader_num_parts(r);
    if (encode) {
        CHECK(exr_writer_create(NULL, &w));
        for (int p = 0; p < nparts; p++) CHECK(exr_writer_add_part(w, exr_reader_part_header(r, p), NULL));
        /* Reencode with the same codec, so scanline block boundaries are unchanged. */
        CHECK(exr_writer_begin_stream_file(w, argv[3], exr_reader_part_header(r, 0)->compression));
    } else {
        f = fopen(argv[3], "wb"); if (!f) return 2;
        u32(f, (uint32_t)nparts);
    }
    for (int p = 0; p < nparts; p++) {
        const exr_header *h = exr_reader_part_header(r, p);
        uint32_t nblocks; CHECK(exr_reader_num_blocks(r, p, &nblocks));
        if (!encode) {
            u32(f, h->part_type); u32(f, (uint32_t)h->num_channels);
            for (int c = 0; c < h->num_channels; c++) {
                const exr_channel *ch = &h->channels[c];
                text(f, ch->name); u32(f, ch->pixel_type); u32(f, ch->x_sampling); u32(f, ch->y_sampling); u32(f, ch->p_linear);
            }
            u32(f, nblocks);
        }
        for (uint32_t b = 0; b < nblocks; b++) {
            exr_block_info info; CHECK(exr_reader_block_info(r, p, b, &info));
            if (!encode) {
                u32(f, info.level_x); u32(f, info.level_y); u32(f, info.x0); u32(f, info.y0);
                u32(f, info.width); u32(f, info.height); u32(f, info.is_deep);
            }
            void **channels = (void **)allocate((size_t)h->num_channels * sizeof(void *));
            int32_t *counts = NULL;
            if (info.is_deep) {
                size_t pixels = (size_t)info.width * info.height; uint64_t total = 0;
                counts = (int32_t *)allocate(pixels * sizeof(int32_t));
                CHECK(exr_reader_decode_deep_counts(r, p, b, counts));
                if (!encode) { u32(f, (uint32_t)pixels); for (size_t i = 0; i < pixels; i++) u32(f, counts[i]); }
                for (size_t i = 0; i < pixels; i++) total += (uint32_t)counts[i];
                for (int c = 0; c < h->num_channels; c++) channels[c] = allocate((size_t)total * (h->channels[c].pixel_type == EXR_PIXEL_HALF ? 2 : 4));
                CHECK(exr_reader_decode_deep_samples(r, p, b, channels));
                if (!encode) for (int c = 0; c < h->num_channels; c++)
                    blob(f, channels[c], (size_t)total * (h->channels[c].pixel_type == EXR_PIXEL_HALF ? 2 : 4));
            } else {
                void *raw = allocate(info.uncompressed_size);
                CHECK(exr_reader_decode_block(r, p, b, raw, info.uncompressed_size));
                if (!encode) blob(f, raw, info.uncompressed_size);
                if (encode) for (int c = 0; c < h->num_channels; c++) {
                    const exr_channel *ch = &h->channels[c];
                    size_t size = samples(info.x0, info.width, ch->x_sampling) * samples(info.y0, info.height, ch->y_sampling) * (ch->pixel_type == EXR_PIXEL_HALF ? 2 : 4);
                    channels[c] = allocate(size);
                    CHECK(exr_block_extract_channel(h, &info, raw, info.uncompressed_size, c, channels[c]));
                }
                free(raw);
            }
            if (encode) {
                if (info.is_deep && info.is_tiled) CHECK(exr_writer_write_deep_tile(w, p, info.tile_x, info.tile_y, info.level_x, info.level_y, counts, (const void *const *)channels));
                else if (info.is_deep) CHECK(exr_writer_write_deep_scanline_block(w, p, info.y0, counts, (const void *const *)channels));
                else if (info.is_tiled) CHECK(exr_writer_write_tile(w, p, info.tile_x, info.tile_y, info.level_x, info.level_y, (const void *const *)channels));
                else CHECK(exr_writer_write_scanline_block(w, p, info.y0, (const void *const *)channels));
            }
            for (int c = 0; c < h->num_channels; c++) free(channels[c]);
            free(channels); free(counts);
        }
    }
    if (encode) { CHECK(exr_writer_end_stream(w)); exr_writer_destroy(w); }
    else if (fclose(f)) return 2;
    exr_reader_close(r); return 0;
}
