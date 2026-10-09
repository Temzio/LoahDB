# ADR-6: Phase 6 — Authenticated encryption (AES-GCM, KDF, key rotation)

## Status

Accepted (Phase 6)

## Context

Phase 0 used AES-CBC with a zero-padded UTF-8 passphrase and no authentication. Page-file stores (Phase 1+) kept document bytes on disk in plaintext. The header page already reserved 32 bytes for a KDF salt at offset 32.

## Decisions

### Key derivation

Passphrases are stretched with **PBKDF2-HMAC-SHA256** (100,000 iterations by default, `LoahOptions.KeyDerivationIterations`) over the per-database **32-byte salt** stored in the page-file header (or embedded in legacy JSON ciphertext).

### AES-GCM for application data

- **JSON / `.loah` files** use a versioned string prefix `LOAH2:` + Base64(`nonce ‖ tag ‖ ciphertext`).
- **Legacy AES-CBC** payloads (no prefix) remain decryptable for compatibility.
- **Page-file document and catalog payloads** are encrypted at the `ValueEncoding` layer when encryption is enabled: inline values are prefixed with an `E` marker, nonce, ciphertext, and 16-byte GCM tag. Overflow chains store encrypted bytes.

The database **header page stays plaintext** (magic, salt, flags) so tools can identify files; page bodies remain fixed 4096 bytes.

### Enabling encryption

New page-file databases created with `LoahOptions.EncryptionKey` set initialize the header salt and encryption flag. Opening an encrypted database without a key fails on first decrypt. Opening a plaintext database with a key does not auto-encrypt (use `EnableEncryption()` or `RotateEncryptionKey`).

### Key rotation

`LoahStore.RotateEncryptionKey` re-derives a key from a new passphrase, re-encrypts all catalog entries and collection documents inside a single store transaction, and replaces the header salt.

## Trade-offs

- B+Tree keys and index sort keys remain plaintext (only stored JSON payloads are confidential).
- PBKDF2 on every open is avoided by caching the derived key for the process lifetime.
- WAL frames contain encrypted page bytes identical to on-disk ciphertext.

## Consequences

- New types: `LoahEncryptionException`, `LoahOptions.KeyDerivationIterations`.
- `LoahStore.EnableEncryption()`, `LoahStore.RotateEncryptionKey`.
- Tests in `LoahPhase6Should.cs`.
