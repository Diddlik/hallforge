# Hardware compatibility

Status levels match `SupportLevel` in the code: `Unknown` (no definition), `Unverified` (definition exists, read-only), `Supported` (verified on hardware, writes allowed).

## Observed devices

### AKKO keyboard, VID 0x3151 PID 0x4035 (wired USB)

Observed on 2026-10-28, Windows 11 (build 26300), with `hallforge devices probe`. The product string is only "Akko Keyboard". The assumption that this PID is the MOD007B ISO HE is not yet confirmed.

| Interface | Usage page / usage | Max report lengths (in / out / feature) | Purpose (assumed) |
|---|---|---|---|
| 0 | 0x0001 / 0x0006 | | Boot keyboard |
| 1 | 0x0001 / 0x0006, 0x0001 / 0x0002, 0x0001 / 0x0080, 0x000C / 0x0001, 0xFFFF / 0x0001, 0x0001 / 0x0005 | consumer: 3 / 0 / 0 | Keyboard, mouse, consumer, vendor input |
| 2 | 0xFFFF / 0x0002 | 0 / 0 / 65 | Vendor configuration (64-byte feature report plus report ID byte) |

- Release number (bcdDevice): 0x0103.
- This matches the RongYuan interface layout in the monsgeek-akko-linux protocol reference: config on interface 2, usage 0xFFFF / 0x02, 64-byte feature report, report ID 0.
- Support level: `Unverified`. No command has been sent to the device.

### AKKO 2.4G receiver, VID 0x3151 PID 0x4011

The same interface layout was seen, including 0xFFFF / 0x0002 on interface 2. Wireless writes are out of scope for the MVP.

## Open questions

- Does PID 0x4035 belong to the MOD007B ISO HE, and which firmware version does it run?
- Does the firmware answer the documented read-only GET commands identically?
