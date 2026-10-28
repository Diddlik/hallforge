# Hardware compatibility

Status levels match `SupportLevel` in the code: `Unknown` (no definition), `Unverified` (definition exists, read-only), `Supported` (verified on hardware, writes allowed).

## Observed devices

### AKKO MOD007B ISO HE, VID 0x3151 PID 0x4035 (wired USB)

Observed on 2026-10-28, Windows 11 (build 26300), with `hallforge devices probe`. The product string is only "Akko Keyboard". The owner confirmed that this PID is the MOD007B ISO HE. Definition: `devices/akko-mod007b-iso-he.json`.

| Interface | Usage page / usage | Max report lengths (in / out / feature) | Purpose (assumed) |
|---|---|---|---|
| 0 | 0x0001 / 0x0006 | | Boot keyboard |
| 1 | 0x0001 / 0x0006, 0x0001 / 0x0002, 0x0001 / 0x0080, 0x000C / 0x0001, 0xFFFF / 0x0001, 0x0001 / 0x0005 | consumer: 3 / 0 / 0 | Keyboard, mouse, consumer, vendor input |
| 2 | 0xFFFF / 0x0002 | 0 / 0 / 65 | Vendor configuration (64-byte feature report plus report ID byte) |

- Release number (bcdDevice): 0x0103.
- This matches the RongYuan interface layout in the monsgeek-akko-linux protocol reference: config on interface 2, usage 0xFFFF / 0x02, 64-byte feature report, report ID 0.
- Firmware identification (`hallforge devices identify`, 2026-10-28):
  - GET_USB_VERSION gives firmware model id **1778**. The raw response is `8F F2 06 00 00 00 00 70 00`, followed by zeros. Byte 7 (0x70) is our own checksum echoed back, not a version.
  - GET_FEATURE_LIST and GET_MULTI_MAGNETISM come back as the unchanged request. See "Echo behavior" in `protocol-research.md`. The precision is **unknown**.
  - The vendor driver lists id 1778 as `yc3121_mod007b_iso_he_soc_uk_8k`, which confirms the model.
  - Model id 1778 is not in the monsgeek-akko-linux device database. That database lists PID 0x4035 for at least ten other boards, including MOD007B HE ids 847, 1637, 3160 and 3161. This is why definitions match on `firmwareModelIds`.
- Write proof of concept (2026-10-28, Windows 11, wired, vendor driver window closed):
  - `hallforge keys set --experimental` set KeyQ (matrix 8, profile 0) to 3.5 mm actuation. The owner confirmed by typing that Q then triggered near the bottom of its travel.
  - Q was then restored to its previous setting from the vendor driver database (0.9 / 0.9 mm, rapid trigger off, 0.2 / 0.3 mm, deadzone 3.7 mm).
  - Per-key settings cannot be read back, so this test is the only verification available.
- Support level: `Unverified`. Writes need `--experimental`.

### AKKO MOD007B ISO HE 2.4G receiver, VID 0x3151 PID 0x4011

This is the 2.4 GHz receiver of the same keyboard, as confirmed by the owner. The same interface layout was seen, including 0xFFFF / 0x0002 on interface 2. Wireless writes are out of scope for the MVP.

## Open questions

- How do firmware version 0x0070 and the vendor driver version string relate?
- What is the travel precision (0.1, 0.01 or 0.005 mm)? It must be derived by comparing a raw GET_MULTI_MAGNETISM value with the value shown in the AKKO driver.
- What does the firmware use for keys that the vendor driver database does not list? The driver only stores keys that differ from its default.
