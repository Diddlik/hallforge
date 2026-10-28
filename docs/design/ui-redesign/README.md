# UI redesign — design reference

Design mockups for the HallForge UI redesign (dark-first premium control center). They are a visual and interaction reference for the Avalonia implementation, not shipped code.

Interactive canvas: https://claude.ai/artifact/H6gkeECihXZRzrUT37zai4 (private until shared).

The `.dc.html` files are the canvas sources. They need the canvas runtime (`support.js`) and do not render as standalone pages in a browser. `canvas.json` holds the board layout.

| File | Screen |
| --- | --- |
| `Main.dc.html` | Performance: WASD multi-key draft, dark theme (the shared screen; the variants below import it) |
| `Single.dc.html` | Performance: one key selected |
| `Mixed.dc.html` | Performance: mixed values including a key with unknown settings |
| `Light.dc.html` | Performance: light theme |
| `Compact.dc.html` | Performance: 1200×750 with collapsed navigation |
| `Review.dc.html` | Review & Apply dialog, sending progress, partial failure |
| `NoDevice.dc.html` | No device, discovering, unverified model (read-only) |
| `Profiles.dc.html` | Profile library with coverage preview |
| `Mapping.dc.html` | Key Mapping — planned, mock preview only |
| `Lighting.dc.html` | Lighting Studio — planned, mock preview only |

## Design tokens

| Token | Dark | Light |
| --- | --- | --- |
| Background | `#0B0F17` | `#F5F7FB` |
| Surface 1 | `#131A27` | `#FFFFFF` |
| Surface 2 | `#1B2434` | `#E9EDF4` |
| Border | `#2A3446` | `#CDD5E1` |
| Text primary | `#F3F6FC` | `#172033` |
| Text secondary | `#B4C0D2` | `#48566A` |
| Accent (selection, primary action) | `#A78BFA` | `#6D28D9` |
| Accent secondary (drafts, precision) | `#22D3EE` | `#087F98` |
| Success | `#34D399` | `#047857` |
| Warning (unknown, unverified) | `#FBBF24` | `#92400E` |
| Error | `#FB7185` | `#BE123C` |

System font, 4/8 px spacing grid, panel radius 14 px, control radius 8 px.

## Limits of the mockups

- The keyboard is a hand-drawn 65 % ISO-DE approximation. The app must draw it from `devices/*.json` geometry and `layouts/*.json` legends.
- Bounds (0.10–3.40 mm, RT 0.05–2.00 mm), demo key values and the single non-editable key (Fn) are illustrative. Real limits come from the device definition.
- Profile values, backup timestamps and USB IDs are placeholders.
- Key Mapping and Lighting show no working device actions; they stay unavailable until protocol support is verified.
- UI strings are English only in the mockups; the app takes them from `Strings.resx` / `Strings.de.resx`.
