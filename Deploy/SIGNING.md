# Code Signing — Jupiter 1.60

> **STATUS — DEFERRED for the §5.1 Jupiter 1.60 launch.** Mars 1.51 and earlier
> shipped unsigned and the project survived. To get Jupiter 1.60 out the door
> faster (and to gauge whether the SmartScreen warning actually hurts install
> rates before committing to the ongoing ~$10/mo + ID-validation overhead),
> §5.1.B is not on the critical path for 1.60. The scripts + this doc are
> kept in the repo as forward-prep so flipping signing on in a later patch
> (1.60.x or 1.61) is one PR + 1–3 business days of Microsoft identity
> validation. See migration-plan-phase5.md §5.1.B for the deferral rationale
> and activation procedure.

When Jupiter eventually adopts code signing, it will use **Microsoft Trusted Signing** (formerly Azure Code Signing) for Authenticode signatures on `StarDrive.exe`, `SDNative.dll`, and the installer EXE. This gives downloads Microsoft's reputation immediately, so users don't see the "Windows protected your PC" SmartScreen warning when running our installer from a clean browser.

This doc captures the **procurement checklist** and the **GitHub Actions secret setup** so the release.yml workflow (§5.1.D) can sign artefacts unattended once activated.

The actual signing logic lives in [sign-binaries.ps1](sign-binaries.ps1); the post-sign verification gate is [SignedBinaryCheck.ps1](SignedBinaryCheck.ps1).

---

## 1. Procurement (one-time, ~1–3 business days)

The bottleneck is Microsoft's identity validation, not the technical setup. Start this **before** §5.1.D so the release pipeline isn't blocked waiting on the cert.

1. **Azure subscription** — sign in at https://portal.azure.com with the team's release email. If TeamStarDrive doesn't have an active Azure subscription, create a Pay-As-You-Go one. Trusted Signing is ~$10/month + per-signature usage; the bill ends up on this subscription.

2. **Create a Trusted Signing account**
   - Portal → **Trusted Signing Accounts** → Create
   - Region: pick the closest to where the team is based (e.g. East US, West Europe). The endpoint URL embeds the region; record it (e.g. `https://eus.codesigning.azure.net/`).
   - Account name: e.g. `teamstardrive-signing`.

3. **Identity validation** (the slow part)
   - Trusted Signing Account → **Identity Validation** → Create
   - Choose **Public** (commercial software distributed publicly).
   - Submit identity documents: maintainer's government ID + proof of association with the publisher name. Microsoft reviews in 1–3 business days.
   - The publisher name on the validation is what shows up on the signed binary's "Publisher" field in Windows. Make it reflect TeamStarDrive (not a personal name) so users see a recognizable publisher.

4. **Create a certificate profile** (after identity validation completes)
   - Trusted Signing Account → **Certificate Profiles** → Create
   - Profile type: **Public Trust** (Authenticode for end-user-facing binaries).
   - Certificate profile name: e.g. `jupiter-release` — this is referenced by the signing script.

5. **Create a service principal for CI**
   - Portal → **Microsoft Entra ID** → **App registrations** → New registration
   - Name: e.g. `github-actions-trusted-signing`
   - Generate a client secret under the registration's **Certificates & secrets** tab. Note the secret value (it's shown only once).
   - Record: **Tenant ID**, **Client ID** (Application ID), **Client Secret**.

6. **Grant the service principal access**
   - Trusted Signing Account → **Access Control (IAM)** → Add role assignment
   - Role: **Trusted Signing Certificate Profile Signer**
   - Assign to: the app registration created above.

---

## 2. GitHub Actions secrets (one-time)

Once procurement closes, set these as repository secrets (Settings → Secrets and variables → Actions). Names match the env vars [sign-binaries.ps1](sign-binaries.ps1) reads:

| Secret name | Value source |
|---|---|
| `AZURE_TENANT_ID` | App registration → **Directory (tenant) ID** |
| `AZURE_CLIENT_ID` | App registration → **Application (client) ID** |
| `AZURE_CLIENT_SECRET` | The client secret created in step 5 |
| `TRUSTED_SIGNING_ENDPOINT` | e.g. `https://eus.codesigning.azure.net/` |
| `TRUSTED_SIGNING_ACCOUNT` | e.g. `teamstardrive-signing` |
| `TRUSTED_SIGNING_CERT_PROFILE` | e.g. `jupiter-release` |

The `TRUSTED_SIGNING_DLIB_PATH` env var doesn't need to be a secret — it's set by the workflow itself (or by the `azure/trusted-signing-action` if used).

---

## 3. CI integration (§5.1.D release.yml)

Two integration paths in release.yml. The first is what we'll use; the second is a fallback if direct signtool control turns out to be needed.

### Option A — `azure/trusted-signing-action` (recommended)

This GitHub Action wraps everything: it pulls the dlib, writes the metadata JSON, and invokes signtool. Add to release.yml after the build step:

```yaml
- name: Sign game binaries
  uses: azure/trusted-signing-action@v0.5.1
  with:
    azure-tenant-id:        ${{ secrets.AZURE_TENANT_ID }}
    azure-client-id:        ${{ secrets.AZURE_CLIENT_ID }}
    azure-client-secret:    ${{ secrets.AZURE_CLIENT_SECRET }}
    endpoint:               ${{ secrets.TRUSTED_SIGNING_ENDPOINT }}
    trusted-signing-account-name: ${{ secrets.TRUSTED_SIGNING_ACCOUNT }}
    certificate-profile-name:     ${{ secrets.TRUSTED_SIGNING_CERT_PROFILE }}
    files-folder:           game
    files-folder-filter:    exe,dll
    file-digest:            SHA256
    timestamp-rfc3161:      http://timestamp.acs.microsoft.com
    timestamp-digest:       SHA256

# After MakeInstaller.py produces Deploy/upload/BlackBox_Jupiter_<ver>.exe,
# sign that too with the same action pointed at the upload dir.

- name: Verify all signatures
  shell: pwsh
  run: |
    Deploy/SignedBinaryCheck.ps1 `
      game/StarDrive.exe, game/SDNative.dll, `
      Deploy/upload/BlackBox_Jupiter_${{ steps.version.outputs.version }}.exe
```

### Option B — direct `sign-binaries.ps1` call (fallback)

Useful if we hit a quirk with the official action — pulls fewer abstraction layers but requires installing the dlib manually.

```yaml
- name: Install Trusted Signing dlib
  shell: pwsh
  run: |
    nuget install Microsoft.Trusted.Signing.Client -OutputDirectory $env:RUNNER_TEMP/ts
    $dlib = Get-ChildItem -Path $env:RUNNER_TEMP/ts -Recurse -Filter Azure.CodeSigning.Dlib.dll |
            Where-Object { $_.FullName -match '\\bin\\x64\\' } |
            Select-Object -First 1
    "TRUSTED_SIGNING_DLIB_PATH=$($dlib.FullName)" >> $env:GITHUB_ENV

- name: Sign binaries
  shell: pwsh
  env:
    AZURE_TENANT_ID:                ${{ secrets.AZURE_TENANT_ID }}
    AZURE_CLIENT_ID:                ${{ secrets.AZURE_CLIENT_ID }}
    AZURE_CLIENT_SECRET:            ${{ secrets.AZURE_CLIENT_SECRET }}
    TRUSTED_SIGNING_ENDPOINT:       ${{ secrets.TRUSTED_SIGNING_ENDPOINT }}
    TRUSTED_SIGNING_ACCOUNT:        ${{ secrets.TRUSTED_SIGNING_ACCOUNT }}
    TRUSTED_SIGNING_CERT_PROFILE:   ${{ secrets.TRUSTED_SIGNING_CERT_PROFILE }}
  run: |
    Deploy/sign-binaries.ps1 game/StarDrive.exe,game/SDNative.dll
```

---

## 4. Local dry-run (optional)

To validate the signing script's plumbing before procurement closes, sign a binary with a self-signed cert:

```powershell
# Create a one-off self-signed code-signing cert in your user store
$c = New-SelfSignedCertificate `
    -Subject "CN=BlackBox Test Signer" `
    -CertStoreLocation Cert:\CurrentUser\My `
    -KeyUsage DigitalSignature -Type CodeSigningCert

# Build first so game/StarDrive.exe exists, then sign a copy
Copy-Item game\StarDrive.exe game\StarDrive.signed.exe
Deploy\sign-binaries.ps1 game\StarDrive.signed.exe -Thumbprint $c.Thumbprint

# Verify (will succeed for cert-chain validity but the cert isn't trusted by
# the Authenticode policy, so /pa will report a chain error — that's expected
# for self-signed; the script's plumbing works regardless).
Deploy\SignedBinaryCheck.ps1 game\StarDrive.signed.exe

# Clean up
Remove-Item Cert:\CurrentUser\My\$($c.Thumbprint)
Remove-Item game\StarDrive.signed.exe
```

A real Trusted Signing cert chains to a Microsoft-trusted root, so `/pa` returns 0 in CI.

---

## 5. Operational notes

- **Signature longevity**: every signature includes an RFC 3161 timestamp from `http://timestamp.acs.microsoft.com`. Once timestamped, the signature stays valid past the cert's expiry — Windows trusts a signature applied while the cert was valid. Don't skip the timestamp step; un-timestamped signatures break the moment the cert rotates.
- **Cert rotation**: Microsoft rotates Trusted Signing certs every ~3 days. Each new run picks up the current cert automatically; nothing in our config references a thumbprint.
- **Cost monitoring**: usage is per-signature. With ~10 signatures per Jupiter release and patches every few weeks, the per-month cost beyond the base fee should be negligible. Set up an Azure cost alert if there's any concern.
- **Revocation**: if a signing cert ever needs revocation (compromised CI secret, etc.), do it through Trusted Signing → Cert Profiles → Revoke. Then rotate the GitHub `AZURE_CLIENT_SECRET` so the leaked principal can't sign anything new.
