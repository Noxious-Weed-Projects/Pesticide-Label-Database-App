# Rush County Pesticide Labels

A .NET MAUI app for Windows PCs and Android tablets that shows the EPA labels for the pesticides the Rush County, KS Noxious Weed Department uses or sells.

Labels come from EPA's [Pesticide Product Label System (PPLS)](https://www.epa.gov/pesticide-labels/pesticide-product-label-system-ppls-application-program-interface-api) through its public API. No account or key is needed.

> **The app does not replace paper labels.** It tells staff so at every launch, on every screen and whenever something fails. The label on the container is the one to follow; PPLS holds the labels EPA accepted, which may be a different version.

## What v0.1 does

- Lists the department's 33 products (from the county chemical list) with search.
- For each EPA-registered product, shows the PPLS details (registrant, status, signal word, restricted-use flag, active ingredients) and the accepted label PDFs, newest first.
- Opens a label inside the app. On Android the pages are drawn as images with pinch zoom; on Windows the built-in PDF viewer is used. **Open / print** hands the PDF to another app.
- Saves every opened label on the device so it opens without a connection. **Download all labels for offline use** saves the newest label of every product in one go.
- Lets staff enter or correct a product's EPA Reg. No., search PPLS by name, and mark the number as checked against the container.
- Marks products that have no EPA label (adjuvants, surfactants, dyes) so staff know to use the paper label and SDS.

## Before staff rely on it

The county list has product names only. The EPA Reg. Nos. built into the app are name matches from PPLS and are shown as "not yet checked against the container" until someone confirms each one in the app.

| State in the app | Products |
|---|---|
| Number matched by name, unconfirmed (19) | De-Amine 4, Sharda Picloram 24% SL, Alligare 2,4-D LV 6, Diablo, Glyphosate 4 Plus, GLY Star Plus, High Noon, Imitator Aquatic, Makaze, Milestone, Mojave 70 EG, Imazuron, MSM 60, Panoramic 2SL, Picloram 22K, Triumph 22K, Tordon 22K, Piper EZ, Outrider |
| Number needed from the container (5) | Alligare 2,4-D, AquaNeat, Drexel Diuron 4L, Loveland MAD DOG, Loveland Rifle |
| No EPA label found (9) | Bubble Blaster, Hot Mes, Liberate, Photo Blue Dye, Blue Wing, Surf-AC 910, Choice Weathermaster, LI 700, Reign |

Edits are stored on each device separately; v0.1 does not sync between devices.

## Install

Packages are written to `artifacts/` (not kept in git).

**Windows** — `RushCountyPesticideLabels-0.1.0-win-x64.msix` is signed with a self-signed certificate, so each PC must trust that certificate once before the package will install:

1. As an administrator, import `RushCountyPesticideLabels.cer` into **Local Machine → Trusted People**.
2. Double-click the `.msix` and choose **Install**.

**Android** — copy `RushCountyPesticideLabels-0.1.0-android.apk` to the tablet, open it, and allow installing from that source. It supports Android 7 and later on 64-bit ARM and x86 tablets.

## Build

Requires the .NET 11 SDK with the MAUI Windows and Android workloads, the Android SDK and a JDK.

```powershell
dotnet test tests/PesticideLabels.Tests
```

```powershell
dotnet build src/PesticideLabels.App -f net11.0-windows10.0.19041.0
```

Signed MSIX (the certificate subject must equal the `Publisher` in `Platforms/Windows/Package.appxmanifest`):

```powershell
dotnet publish src/PesticideLabels.App -f net11.0-windows10.0.19041.0 -c Release -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=MSIX -p:WindowsAppSDKSelfContained=true -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=<thumbprint> -p:AppxBundle=Never
```

Signed APK (the password file holds the password on two lines, one for the keystore and one for the key):

```powershell
dotnet publish src/PesticideLabels.App -f net11.0-android -c Release -p:AndroidPackageFormat=apk -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<full path>\signing\rushcounty-labels.keystore -p:AndroidSigningKeyAlias=rushcountylabels -p:AndroidSigningStorePass=file:<full path>\signing\keystore-password.txt -p:AndroidSigningKeyPass=file:<full path>\signing\keystore-password.txt
```

Keep `signing/` backed up and out of git. Updates to the Android app must be signed with the same keystore, or tablets will need the app uninstalled first.

## Layout

- `src/PesticideLabels.Core` — PPLS client, the county product list, and on-device storage of details and PDFs.
- `src/PesticideLabels.App` — the MAUI app (Windows and Android).
- `tests/PesticideLabels.Tests` — unit tests for the core library.
