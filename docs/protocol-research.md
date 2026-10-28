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
- The vendor driver stores per-key settings in its own local database (`%AppData%/Akko Cloud Driver/iot_db/CONFIG`, entries such as `"travel":9`, `"liftTravel":9`, `"firePressTravel":2`) instead of reading them from the keyboard.

## USB capture of the vendor driver (2026-10-28, wired MOD007B, model 1778)

The vendor driver was opened on the actuation page while USBPcap was capturing. All 14 exchanges were simple GET commands, and the keyboard answered every one of them. **No 0xE5 (GET_MULTI_MAGNETISM) was sent.** The keyboard echoes our 0xE5, which is consistent with this firmware not supporting per-key reads at all.

| Request | Response (non-zero prefix) | Notes |
|---|---|---|
| 0xFD | `9F 00 .. 60 01 0E 01 09 01 15 01 0F` | Returns the buffer of a previous 0x9F; meaning unknown |
| 0x8F GET_USB_VERSION | `8F F2 06 00 00 00 00 70` | Model id 1778 |
| 0x87 GET_LEDPARAM | `87 13 02 02 07 B4 B4 B4` | |
| 0xF0 | `F0 03 01` | |
| 0x80 GET_REV | `80 01 01` | |
| 0xAE GET_MLED_VERSION | `AE 03 01` | |
| 0x84 GET_PROFILE | `84 00 01` | Active profile 0 |
| 0x85 GET_LEDONOFF | `85` | |
| 0x86 GET_DEBOUNCE | `86 00 10 01` | |
| 0x92 | `92 78 00 78 00 84 03 58 02` | Sleep times? (0x78 = 120, 0x384 = 900, 0x258 = 600) |
| 0x97 GET_AUTOOS_EN | `97` | |
| 0x9D GET_KEY_MAGNETISM_MODE | `9D 03` | |
| 0x9F | `9F 00 .. 60 01 0E 01 09 01 15 01 0F` | |

Consequence: for model 1778, per-key actuation, release and rapid trigger cannot currently be read back from the device. Backups and readback verification (requirements US-02, US-03 and 3.5 step 6) are not possible with the commands known so far.

## Key trigger write, 0x1A (verified against a vendor driver capture, 2026-10-28)

The vendor driver writes one key per report and never reads back. Payload offsets below exclude the report id byte:

| Offset | Meaning |
|---|---|
| 0 | 0x1A |
| 1 | Profile (0-3, from GET_PROFILE 0x84) |
| 2 | 0x01 = normal mode |
| 3 | Option (0 = normal; 2 dks, 3 mod-tap, 4/5 toggle, 6 gamepad are not supported by HallForge), `| 0x80` = rapid trigger on |
| 4 | Actuation in 0.1 mm |
| 5 | "Apply to all keys" flag; HallForge always sends 0 |
| 6 | Matrix index |
| 7 | Bit7 checksum over offsets 0-6 |
| 8 | Release in 0.1 mm |
| 9 / 10 | Rapid trigger press / release sensitivity in 0.1 mm |
| 11-24 | DKS / mod-tap / gamepad fields, 0 in normal mode |
| 25 | Deadzone as `mm * 10 - 1` |

- Captured frames for AltLeft (matrix 23) are golden tests in `RongYuanClientTests`.
- The vendor driver also sent a SET_KEYMATRIX_SIMPLE (0x13) in that session that rebinds AltLeft to its default 0xE2. HallForge never sends 0x13.
- Default limits in the vendor driver: travel 0.1-4.0 mm, rapid trigger 0.1-2.5 mm, deadzone 0.1-4.0 mm (default 3.7), all in 0.1 mm steps. They are stored in the device definition.
- The default key matrix of the model's driver class maps matrix index to HID usage. It is stored as `keys` (W3C `KeyboardEvent.code` names) in `devices/akko-mod007b-iso-he.json`. Fn (59), index 71 and the encoder (90, 91, 96, 97) are excluded.

## Next steps (Phase 1)

1. Done: proof of concept on KeyQ (3.5 mm, confirmed by typing, then restored to 0.9 mm). See `hardware-compatibility.md`.
2. Import the current per-key state from the vendor driver database as the initial backup.
3. Keep the last applied state locally, because the device cannot report it.
