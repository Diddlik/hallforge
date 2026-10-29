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
| 0xFD | `9F 00 .. 60 01 0E 01 09 01 15 01 0F` | Returns the buffer of the previous 0x9F; meaning unknown |
| 0x8F GET_USB_VERSION | `8F F2 06 00 00 00 00 70` | Model id 1778 |
| 0x87 GET_LEDPARAM | `87 13 02 02 07 B4 B4 B4` | |
| 0xF0 | `F0 03 01` | |
| 0x80 GET_REV | `80 01 01` | |
| 0xAE GET_MLED_VERSION | `AE 03 01` | |
| 0x84 GET_REPORT | `84 00 01` | Report rate; not the profile (see Onboard profiles) |
| 0x85 GET_PROFILE | `85 00 .. 7A` | Unchanged request = profile 0 (see Onboard profiles) |
| 0x86 GET_KBOPTION | `86 00 10 01` | Keyboard options of profile 0 (see Roar mode) |
| 0x92 | `92 78 00 78 00 84 03 58 02` | Sleep times? (0x78 = 120, 0x384 = 900, 0x258 = 600) |
| 0x97 GET_AUTOOS_EN | `97` | |
| 0x9D GET_KEY_MAGNETISM_MODE | `9D 03` | Travel mode Custom |
| 0x9F GET_MAGNETISM_FAST | `9F 00 .. 60 01 0E 01 09 01 15 01 0F` | Roar mode: W, A, S, D enabled |

Consequence: for model 1778, per-key actuation, release and rapid trigger cannot currently be read back from the device. Backups and readback verification (requirements US-02, US-03 and 3.5 step 6) are not possible with the commands known so far.

## Key trigger write, 0x1A (verified against a vendor driver capture, 2026-10-28)

The vendor driver writes one key per report and never reads back. Payload offsets below exclude the report id byte:

| Offset | Meaning |
|---|---|
| 0 | 0x1A |
| 1 | Profile (0-3, from GET_PROFILE 0x85) |
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

## Lighting (GET_LEDPARAM 0x87, verified read)

Response bytes (byte 0 = echo): 1 mode (0 off ... 24 fireworks, see `LightingMode`), 2 = 4 - speed, 3 brightness, 4 = option << 4 | color mode (7 = rainbow), 5-7 RGB. Model 1778 answers it (recorded `87 13 02 02 07 B4 B4 B4`: press-action-off, brightness 2, speed 2, rainbow).

Writing (SET_LEDPARAM 0x07, verified by vendor driver capture `ws/usb4`): `[07, mode, 4 - speed, brightness, option << 4 | color mode, R, G, B, checksum]` with the **Bit8** checksum (255 minus the low byte of the sum of bytes 0-7, stored at byte 8). Color mode 8 = fixed color, 7 = rainbow. Brightness and speed are 0-4. The vendor driver substitutes #FAFFFA for white and waits 500 ms after the write. HallForge reads the lighting back after every write and fails if it differs, so lighting writes are verified, unlike key settings. User picture, music and screen color modes are not writable because they need data HallForge does not provide.

Option values the vendor driver writes for model 1778 (`LightingState.VariantsOf`, from the driver's lighting encoder, not individually tested on hardware): Wave 0 right, 1 left, 2 down, 3 up; Snake 0 zigzag, 1 back and forth; Kaleidoscope 0 outward, 1 inward; Line wave and Fireworks 0 right, 1 left; Circle wave 0 counterclockwise, 1 clockwise. All other writable effects use option 0.

## Roar mode (GET 0x9F / SET 0x1F, verified read and write, 2026-10-29)

The vendor driver calls it "guansu" (光速) internally and shows it as "Roar Setting" for models with `otherSetting.isguansu`, which includes model 1778. Its help text: the fastest response for up to four keys; the backlight is turned off while Roar is on.

- GET_MAGNETISM_FAST 0x9F: the response repeats the request bytes 0-7 (command, zeros, our checksum 0x60) and adds four slots `[enabled, matrix index]` at bytes 8-15. With all slots empty it equals the request, so `ReadRoar` accepts two identical reads instead of using echo detection.
- SET_MAGNETISM_FAST 0x1F (capture `ws/usb5`): `1F 00 00 00 00 00 00 E0` then the four slots. The Bit7 checksum covers only bytes 0-6, not the slots. An empty slot is `00 00`; a disabled slot keeps its key (`00 0E`).
- The setting is global; the command has no profile byte.
- After any Roar change, the vendor driver writes SET_KBOPTION 0x06 `[06, profile, flags, fn layout]` with bit 4 of the flags ("LED off") set while any slot is enabled. GET_KBOPTION 0x86 `[86, profile]` returns `86 00 10 01` on model 1778. Flag bits per the driver: 0 Win lock, 2 Mac mode, 3 WASD/arrow swap, 4 LED off, 5 side LED off, 6 keyboard mode, 7 lock. HallForge changes only bit 4, writes the rest back as read, and refuses responses with unknown non-zero bytes 4-6.
- Right after a SET, the firmware keeps answering with the previous response for up to a few hundred ms, so reads after writes poll until the value appears.
- Verified on hardware with `hallforge roar set --experimental`: cleared all slots (backlight on), restored W, A, D, S (backlight off), twice, each read back.

## Command table of the model's driver class

The vendor driver's base command table that matches the verified commands of model 1778 (KBOPTION 0x06/0x86, LEDPARAM 0x07/0x87) lists: SET_REPORT 0x04 / GET_REPORT 0x84, SET_PROFILE 0x05 / GET_PROFILE 0x85, SET_KEYMATRIX 0x09, SET_MACRO 0x0B, SET_FN 0x10, SET_KEYMATRIX_SIMPLE 0x13, SET_FN_SIMPLE 0x15 and others. HallForge read the profile with 0x84 until 2026-10-29; that is GET_REPORT, whose byte 1 also happened to be 0.

## Onboard profiles (GET 0x85 / SET 0x05, verified, 2026-10-29)

- GET_PROFILE 0x85: byte 1 is the active profile 0-3. Profile 0 comes back as the unchanged request (`85 00 00 00 00 00 00 7A`, vendor capture `ws/usb2`), so profile 0 cannot be told from an unsupported command; the client accepts two identical reads.
- SET_PROFILE 0x05 `[05, profile]` with the Bit7 checksum, as in the driver's encoder. Not captured from the vendor driver (it shows no profile selector for this model, and the manual names no profile shortcut), but taken from the matching command table.
- Verified on hardware with `hallforge profile set --experimental`: switched from profile 1 to 2 (read back as byte 1 = 1) and back to 1.

## Travel mode (GET 0x9D / SET 0x1D, verified, 2026-10-29)

- The manual's Fn+1 to Fn+4 shortcuts: Fn+1 2 mm (Comfort), Fn+2 0.5 mm (Sensitive), Fn+3 0.5 mm plus "Ultra Sensitive Mode" (Gaming; the Chinese text says 火力模式, the driver's name for non-stop rapid trigger), Fn+4 the per-key settings from the driver (Custom, 2 mm by default).
- GET_KEY_MAGNETISM_MODE 0x9D: byte 1 = mode 0-3, byte 2 keyboard disabled, byte 3 gamepad enabled (vendor decoder).
- SET_KEY_MAGNETISM_MODE 0x1D `[1D, mode]`, Bit7 checksum (capture `ws/usb6`: `1d000000000000e2` to `1d030000000000df`).
- Per-key settings written with 0x1A take effect only in Custom.
- Verified on hardware: Custom to Comfort and back, each read back.

## Other manual facts (MOD007B HE user manual, file.akkogear.com/MOD007B_HE.pdf)

Fn+L backlight on/off, Fn+W swaps WASD and the arrow keys, Fn+Left Win locks the Win key, Fn+Home/PgUp/End/PgDn select lighting effect groups, Fn+\ cycles colors, holding Fn+~ for 5 s restores factory settings. These match bits of KBOPTION (LED off, WASD swap, Win lock).

## Non-stop rapid trigger (vendor driver, not yet tested on hardware)

The vendor driver labels the 0x1A rapid trigger flag (`fire`, option byte `| 0x80`) "Non-stop Rapid Trigger": "When enabled, rapid trigger will only stop when you fully release the key", otherwise it stops once the key rises above the actuation point. The driver has no separate switch for plain rapid trigger. Whether the RT sensitivities take effect without the flag is still to be tested by typing.

## Next steps (Phase 1)

1. Done: proof of concept on KeyQ (3.5 mm, confirmed by typing, then restored to 0.9 mm). See `hardware-compatibility.md`.
2. Done: `backup import-akko` imports the vendor driver state. The newest `devConfig_<modelId>` value in the log-structured database is the last one in the file, which was verified against today's AltLeft change. Array index = onboard profile.
3. Done: `StateStore` keeps the last applied state.
