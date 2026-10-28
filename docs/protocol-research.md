# Protocol research

## Sources

- monsgeek-akko-linux `docs/PROTOCOL.md` (GPL-3.0, compatible with this project's license): https://github.com/echtzeit-solutions/monsgeek-akko-linux/blob/master/docs/PROTOCOL.md

## RongYuan family (AKKO, MonsGeek and others)

Documented facts, not yet verified on our hardware unless noted:

- Vendor IDs: 0x3151 (MonsGeek/Akko), 0x145F (Akko alternate). The reference also lists Epomaker 0x342D, Feker 0x347A, Womier 0x374A, DrunkDeer 0x379A and Cherry 0x25AA, but none of these are confirmed to use the same protocol.
- Config interface: interface 2, usage page 0xFFFF, usage 0x02, report ID 0, 64-byte feature reports. **Verified on PID 0x4035 (descriptor only).**
- Read-only candidates for the first probe: 0x80 GET_REV, 0x8F GET_USB_VERSION, 0xD0 GET_SKU, 0xE6 GET_FEATURE_LIST, 0xE5 GET_MULTI_MAGNETISM.
- Per-key magnetism: 0xE5 GET / 0x65 SET_MULTI_MAGNETISM.
- Blocked for the MVP: SET_KEYMATRIX, SET_MACRO, SET_FN, every firmware or bootloader command, and every chunked SET command.

## Verified framing (wired, MOD007B ISO HE)

- Request: a 65-byte feature report laid out as `[0x00 report id][cmd][p1..p6][checksum][zeros]`. The checksum is `255 - (sum(cmd, p1..p6) & 0xFF)`.
- Response: GET_FEATURE with report id 0. Byte 1 echoes the command. If the echo does not match, the client retries up to 5 times with a 10 ms gap between send and read.
- GET_USB_VERSION layout: bytes 1-4 hold the model id (u32 LE), and bytes 7-8 hold the firmware version (u16 LE). Verified against model id 1778.
- Implemented in `HallForge.Protocol.RongYuan.RongYuanClient`. The allowlist is the `ReadCommand` enum, which is also checked at runtime.

## Next steps (Phase 1)

1. Read one GET_MULTI_MAGNETISM page (press travel) and compare the raw values with the AKKO driver to derive the precision factor.
2. Map the matrix index to key ids for the ISO-DE layout.
