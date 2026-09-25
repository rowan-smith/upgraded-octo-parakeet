# ForgeDeck signing keys

| File | Purpose |
|------|---------|
| `licence-public.pem` | Verifies `forgedeck.lic` signatures — **Ed25519** (also embedded in `ForgeDeckLicenceKeys`) |
| `licence-private.pem` | **Never commit.** Signs licence files in release CI (Ed25519 PKCS#8 PEM) |
| `package-public.pem` | Verifies commercial extension package envelopes — **RSA-2048** (also embedded in `ForgeDeckPackageKeys`) |
| `package-private.pem` | **Never commit.** Signs commercial packages in release CI |

Private key files are gitignored.

## Licence keys (Ed25519)

```bash
# Sign a licence document
dotnet run --project tools/LicenceSigner -- keys/licence-private.pem unsigned.json forgedeck.lic
```

Generate a replacement pair (PKCS#8 private + SPKI public PEM), update `keys/licence-*.pem` and the embedded constant in `ForgeDeckLicenceKeys`.

## Package keys (RSA)

Still RSA-SHA256 PKCS#1 for extension package envelopes. Generate RSA-2048 PKCS#1 PEM and update `ForgeDeckPackageKeys`.
