# Offline Boxicons

Copy the Boxicons distribution into this directory for the air-gapped deployment.

Required files:

- `css/boxicons.min.css`
- `fonts/boxicons.woff2`
- `fonts/boxicons.woff`
- `fonts/boxicons.ttf`

The CSS file must keep its normal relative font URLs, so the directory layout should be:

`wwwroot/lib/boxicons/css/boxicons.min.css`

`wwwroot/lib/boxicons/fonts/boxicons.woff2`

`wwwroot/lib/boxicons/fonts/boxicons.woff`

`wwwroot/lib/boxicons/fonts/boxicons.ttf`

No CDN is required or used.

## Version

This directory holds the official Boxicons 2.1.4 distribution (npm package `boxicons@2.1.4`,
`css/boxicons.min.css` and `fonts/boxicons.{woff2,woff,ttf}`). The CSS and the font files must come
from the same release: a font with a different glyph set makes icons show the wrong shape or nothing.

The only local change is a `?v=2.1.4` query string on the three font URLs inside the CSS, so browsers
reload the fonts after an upgrade. Bump it (and the `?v=` on the link in `Components/App.razor`)
whenever these files are replaced.
