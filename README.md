# Rush County Pesticide Labels

A .NET MAUI app for Windows PCs and Android tablets that shows the EPA labels for the pesticides the Rush County, KS Noxious Weed Department uses or sells.

Labels come from EPA's [Pesticide Product Label System (PPLS)](https://www.epa.gov/pesticide-labels/pesticide-product-label-system-ppls-application-program-interface-api) through its public API. No account or key is needed.

On Windows the app also shows Safety Data Sheets from the [CDMS Label Database](https://www.cdms.net/labelssds/home/).

> **The app does not replace paper labels.** It tells staff so at every launch, on every screen and whenever something fails. The label on the container is the one to follow; PPLS holds the labels EPA accepted, which may be a different version.

## What v0.1 does

- Lists the department's 33 products (from the county chemical list) with search.
- For each EPA-registered product, shows the PPLS details (registrant, status, signal word, restricted-use flag, active ingredients) and the accepted label PDFs, newest first.
- Opens a label inside the app. On Android the pages are drawn as images with pinch zoom; on Windows the built-in PDF viewer is used. **Open / print** hands the PDF to another app.
- Saves every opened label on the device so it opens without a connection. **Download all labels for offline use** saves the newest label of every product in one go.
- Lets staff enter or correct a product's EPA Reg. No., search PPLS by name, and mark the number as checked against the container.
- Marks products that have no EPA label (adjuvants, surfactants, dyes) so staff know to use the paper label and SDS.

## Safety Data Sheets (Windows, since v0.1.1)

Each product page on Windows has a **Safety Data Sheet** section below the labels.

- The app searches CDMS by product name. If exactly one US product there has the same EPA Reg. No., its sheets are shown straight away.
- Otherwise the app lists the CDMS products with a similar name and staff choose the one on the container. This is how products with no EPA label (adjuvants, surfactants, dyes) get a sheet. The choice is remembered and can be changed.
- An opened sheet is saved on the PC and opens without a connection afterwards. **Download all labels for offline use** does not fetch Safety Data Sheets.
- CDMS only has products whose manufacturer posts them there. When a product is missing, the app says so and staff use the paper SDS.

CDMS has no published API. The app calls the same addresses the CDMS web page calls (`Home/ProductList`, `Home/DocumentList` and the PDFs under `ldat/`), so a redesign of that site can break this feature until the app is updated. Check CDMS's terms of use before distributing the app outside the department.

The Android version does not have this section yet.

## Before staff rely on it

The county list has product names only. The EPA Reg. Nos. built into the app are name matches from PPLS and are shown as "not yet checked against the container" until someone confirms each one in the app.

| State in the app | Products |
|---|---|
| Number matched by name, unconfirmed (19) | De-Amine 4, Sharda Picloram 24% SL, Alligare 2,4-D LV 6, Diablo, Glyphosate 4 Plus, GLY Star Plus, High Noon, Imitator Aquatic, Makaze, Milestone, Mojave 70 EG, Imazuron, MSM 60, Panoramic 2SL, Picloram 22K, Triumph 22K, Tordon 22K, Piper EZ, Outrider |
| Number needed from the container (5) | Alligare 2,4-D, AquaNeat, Drexel Diuron 4L, Loveland MAD DOG, Loveland Rifle |
| No EPA label found (9) | Bubble Blaster, Hot Mes, Liberate, Photo Blue Dye, Blue Wing, Surf-AC 910, Choice Weathermaster, LI 700, Reign |

Edits are stored on each device separately; the app does not sync between devices yet.

## Planned

**Tablet updates from a central server (not built yet).** A later version will let the Android tablets get their updates from a server the department sets up, instead of each tablet being updated by hand. Nothing in the current code does this. Still to be decided before it is built:

- What the server hands out: the product list and EPA Reg. No. corrections, the label PDFs and Safety Data Sheets, new versions of the app itself, or all of these.
- Where the server runs (a PC in the shop or a hosted service) and whether tablets reach it only on the shop network.
- How a tablet proves it belongs to the department, and how the server's files are protected from tampering.
- What happens to edits made on a tablet when the server's copy differs.

## Install

Packages are written to `artifacts/` (not kept in git).

**Windows** — `RushCountyPesticideLabels-0.1.1-win-x64.msix` is signed with a self-signed certificate, so each PC must trust that certificate once before the package will install:

1. As an administrator, import `RushCountyPesticideLabels.cer` into **Local Machine → Trusted People**.
2. Double-click the `.msix` and choose **Install**. On a PC that already has an earlier version, this updates it and keeps the saved labels and edits.

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

- `src/PesticideLabels.Core` — PPLS and CDMS clients, the county product list, and on-device storage of details and PDFs.
- `src/PesticideLabels.App` — the MAUI app (Windows and Android).
- `tests/PesticideLabels.Tests` — unit tests for the core library.
