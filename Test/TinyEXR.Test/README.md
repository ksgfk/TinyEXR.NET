# TinyEXR.Test

## Test Naming

- Keep existing upstream-style or regression-style `DisplayName` values unchanged when the test is directly mirroring upstream tinyexr coverage.
- For tests added by TinyEXR.NET beyond upstream coverage, prefix the `DisplayName` with `[TinyEXR.NET Test] `.
- Keep the remainder of the `DisplayName` concise and sample-oriented so it still groups naturally with the related file or feature.

## Example

- `[TinyEXR.NET Test] deepscanline.exr|LoadDeep`
