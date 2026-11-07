/* Test-only oracle for the pinned TinyEXR v3 implementation. */
#include "exr_internal.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static unsigned char *read_file(const char *path, size_t *size) {
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return NULL; }
    long length = ftell(f);
    if (length < 0 || fseek(f, 0, SEEK_SET) != 0) { fclose(f); return NULL; }
    *size = (size_t)length;
    unsigned char *p = (unsigned char *)malloc(*size ? *size : 1);
    if (!p || fread(p, 1, *size, f) != *size) { free(p); fclose(f); return NULL; }
    fclose(f);
    return p;
}

int main(int argc, char **argv) {
    /* mode input output compression x y width height raw-size type:xs:ys:linear ... */
    if (argc < 11) return 2;
    exr_codec_ctx ctx = {0};
    ctx.alloc = exr_default_allocator();
    exr_channel *channels = (exr_channel *)calloc((size_t)(argc - 10), sizeof(exr_channel));
    if (!channels) return 2;
    for (int i = 10; i < argc; i++) {
        int type, xs, ys, linear;
        if (sscanf(argv[i], "%d:%d:%d:%d", &type, &xs, &ys, &linear) != 4) return 2;
        channels[i - 10].pixel_type = (exr_pixel_type)type;
        channels[i - 10].x_sampling = xs;
        channels[i - 10].y_sampling = ys;
        channels[i - 10].p_linear = linear;
    }
    ctx.compression = (exr_compression)atoi(argv[4]);
    ctx.x = atoi(argv[5]); ctx.y = atoi(argv[6]);
    ctx.width = atoi(argv[7]); ctx.num_lines = atoi(argv[8]);
    ctx.channels = channels; ctx.num_channels = argc - 10;
    size_t input_size = 0, output_size = (size_t)strtoull(argv[9], NULL, 10);
    unsigned char *input = read_file(argv[2], &input_size), *output = NULL;
    if (!input) { free(channels); return 2; }
    exr_result result;
    if (strcmp(argv[1], "encode") == 0) {
        result = exr_compress_block(&ctx, input, input_size, &output, &output_size);
    } else {
        output = (unsigned char *)malloc(output_size ? output_size : 1);
        if (!output) { free(input); free(channels); return 2; }
        result = exr_decompress_block(&ctx, input, input_size, output, output_size);
    }
    printf("%d\n", (int)result);
    int status = 0;
    if (result == EXR_SUCCESS) {
        FILE *f = fopen(argv[3], "wb");
        if (!f) status = 2;
        else { if (fwrite(output, 1, output_size, f) != output_size) status = 2; fclose(f); }
    } else status = 1;
    free(output); free(input); free(channels);
    return status;
}
