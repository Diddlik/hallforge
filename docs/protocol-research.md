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
- GET_USB_VERSION layout: bytes 1-4 hold the model id (u32 LE). Verified: model id 1778.
- **Echo behavior (model 1778):** the firmware answers commands it does not handle by returning the request unchanged (command, parameters and checksum). Observed for:
  - GET_FEATURE_LIST.
  - GET_MULTI_MAGNETISM, for every table and page, even after waiting 780 ms.
  - GET_USB_VERSION, where only the model id bytes are overwritten. The version bytes 7-8 contain our own checksum, so model 1778 reports no firmware version.
- The client treats an unchanged request as "not ready" and polls with growing delays (10 to 400 ms). If the request is still unchanged after that, it raises `CommandNotSupportedException`. Echoes are never accepted as data.
- Implemented in `HallForge.Protocol.RongYuan.RongYuanClient`. The allowlist is the `ReadCommand` and `MagnetismTable` enums, which are also checked at runtime. Magnetism pages are capped at 0-3.

## Vendor driver findings (Akko Cloud Driver v3, `resources/app/dist/static/js/main_*.js`)

- Model 1778 is listed as `yc3121_mod007b_iso_he_soc_uk_8k` ("MOD007B ISO HE"): USB 0x3151:0x4035, usage 0xFFFF / 0x02, 65-byte feature reports, 8 layers, magnetism supported. The 2.4 GHz variant uses id `0x80000000 | 1778` on PID 0x4011.
- The driver class for this model only overrides the default key matrix. Everything else comes from the shared RongYuan base class.
- The base class reads per-key tables with the same request as ours: `[0xE5, table, 1, page]` with a Bit7 checksum, 2 pages per table, one value per matrix position. The table ids match: 0 press, 1 lift, 2 RT press, 3 RT lift, 6 deadzone, 7 key mode.
- Bytes are sent by the native `iot_driver.exe`, not by the JS, so the exact framing on the wire is not visible there.
- Open question: why does the keyboard echo our identical request? The next step is a USB capture of the vendor driver (USBPcap and Wireshark) while it reads the key settings.

## Next steps (Phase 1)

1. Capture the vendor driver's traffic to find what it does differently. Then read one press-travel page and compare it with the values shown in the AKKO driver to derive the precision factor.
2. Map the matrix index to key ids for the ISO-DE layout.
