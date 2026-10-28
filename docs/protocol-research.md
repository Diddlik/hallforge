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

## Next steps (Phase 1)

1. Encode GET_REV / GET_USB_VERSION, send them to PID 0x4035 only, and record the raw responses as golden fixtures.
2. Confirm the checksum and the response format before adding any further command.
