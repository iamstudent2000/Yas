# Offline Persian fonts

This folder is intentionally kept free of binary font files in Git.

For the air-gapped deployment, copy these Vazirmatn WOFF2 files into this folder:

- `Vazirmatn-Regular.woff2`
- `Vazirmatn-SemiBold.woff2`
- `Vazirmatn-Bold.woff2`

The application references them from `/fonts/...`.

Recommended source: the official Vazirmatn distribution you have approved for your environment. Keep the filenames exactly as listed above.

## Optional fonts for the appearance settings

Users can pick Sahel or Shabnam under «ظاهر برنامه». Both are SIL OFL fonts by Saber Rastikerdar. Copy these into this folder too
(the pages still work without them — the browser falls back to the system font):

- `Sahel-Regular.woff2`, `Sahel-SemiBold.woff2`, `Sahel-Bold.woff2`
- `Shabnam-Regular.woff2`, `Shabnam-Medium.woff2`, `Shabnam-Bold.woff2`

Source: the `sahel-font` and `shabnam-font` packages (their `dist/` folders; `Sahel.woff2` and `Shabnam.woff2` are the regular weights,
renamed). Keep the OFL license files with the fonts.
