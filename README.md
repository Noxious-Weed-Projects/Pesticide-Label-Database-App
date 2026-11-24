# Rush County Pesticide Labels

A .NET MAUI app for Windows PCs and Android tablets that shows the EPA labels for the pesticides the Rush County, KS Noxious Weed Department uses or sells.

Labels come from EPA's [Pesticide Product Label System (PPLS)](https://www.epa.gov/pesticide-labels/pesticide-product-label-system-ppls-application-program-interface-api) through its public API. No account or key is needed.

The app also shows each product's Safety Data Sheet and the manufacturer's label from the [CDMS Label Database](https://www.cdms.net/labelssds/home/). That is where labels for products EPA's API does not have (adjuvants, surfactants, dyes) come from.

> **The app does not replace paper labels.** It tells staff so at every launch, on every screen and whenever something fails. The label on the container is the one to follow; PPLS holds the labels EPA accepted, which may be a different version.

## What v0.1 does

- Lists the department's products (from the county chemical list) with search.
- For each EPA-registered product, shows the PPLS details (registrant, status, signal word, restricted-use flag, active ingredients) and the accepted label PDFs, newest first.
- Opens a label inside the app. On Android the pages are drawn as images with pinch zoom; on Windows the built-in PDF viewer is used. **Open / print** hands the PDF to another app.
- Saves every opened label on the device so it opens without a connection. **Download all labels for offline use** saves the newest label of every product in one go.
- Lets staff enter or correct a product's EPA Reg. No., search PPLS by name, and mark the number as checked against the container.
- Marks products that have no EPA label (adjuvants, surfactants, dyes) so staff know to use the paper label and SDS.

## What v0.2.0 adds

- Safety Data Sheets from CDMS on Android as well as Windows.
- The manufacturer's label from CDMS on both, including for products EPA's API has no label for.
- On Android, saved labels and Safety Data Sheets that have not been opened for seven days are removed to save storage.

## What v0.2.1 changes

- The product list follows the updated county chemical list: 34 products, with **Cornbelt 4 Lb. Amine** added.
- EPA Reg. Nos. from the county list for Alligare 2,4-D (81927-38), AquaNeat (228-365), Drexel Diuron 4L (19713-36), Loveland MAD DOG (34704-889) and Loveland Rifle (34704-861).
- A device that already has an earlier version gets the new product and these numbers when it is updated. Numbers staff entered or confirmed on the device are kept.

The county list shows 19713-36 beside Diablo. The department confirmed it is Drexel Diuron 4L's number, which is also how EPA registers it, so the app puts it there and Diablo keeps 228-379.

## What v0.2.2 changes

- EPA Reg. No. from the county list for Cornbelt 4 Lb. Amine (11773-2). Every EPA-registered product on the list now has a number.
- A device that already has an earlier version gets this number when it is updated, unless staff already entered one there.

## Labels and Safety Data Sheets from CDMS

Each product page has a **Label and Safety Data Sheet from CDMS** section below the EPA labels, on Windows and Android.

- The app searches CDMS by product name. If exactly one US product there has the same EPA Reg. No., its documents are shown straight away.
- Otherwise the app lists the CDMS products with a similar name and staff choose the one on the container. This is how products with no EPA label (adjuvants, surfactants, dyes) get a label and a sheet. The choice is remembered and can be changed.
- **Manufacturer's label** lists the product's specimen label. Supplemental documents (24(c) registrations, 2(ee) recommendations and the like) are behind **Show supplemental label documents**; most of them are for other states.
- **Safety Data Sheet** lists the product's sheets.
- An opened document is saved on the device and opens without a connection afterwards. **Download all labels for offline use** fetches EPA labels only, not CDMS documents.
- CDMS only has products whose manufacturer posts them there. When a product is missing, the app says so and staff use the paper label and SDS.

A CDMS label is the manufacturer's specimen label, which can differ from both the label EPA accepted and the one on the container. The label on the container is the one to follow.

CDMS has no published API. The app calls the same addresses the CDMS web page calls (`Home/ProductList`, `Home/DocumentList` and the PDFs under `ldat/`), so a redesign of that site can break this feature until the app is updated. Check CDMS's terms of use before distributing the app outside the department.

## Storage on tablets and phones (Android, since v0.2.0)

To save storage, the Android app deletes a saved label or Safety Data Sheet once seven days have passed since it was last downloaded or opened. It checks each time the app is started or brought back to the screen.

- Opening a document, or pressing **Download all labels for offline use**, keeps it for another seven days.
- The product list, EPA Reg. Nos., product details and CDMS choices are never removed.
- A removed document shows "(needs internet)" and is downloaded again when opened. **Without a connection it cannot be opened**, so before a week in the field press **Download all labels for offline use** while connected, and keep the paper copies.
- The Windows app does not remove anything.

## Before staff rely on it

The county list gives an EPA number for a few products. The other EPA Reg. Nos. built into the app are name matches from PPLS. All are shown as "not yet checked against the container" until someone confirms each one in the app.

| State in the app | Products |
|---|---|
| Number from the county list, unconfirmed (6) | Alligare 2,4-D, AquaNeat, Drexel Diuron 4L, Loveland MAD DOG, Loveland Rifle, Cornbelt 4 Lb. Amine |
| Number matched by name, unconfirmed (19) | De-Amine 4, Sharda Picloram 24% SL, Alligare 2,4-D LV 6, Diablo, Glyphosate 4 Plus, GLY Star Plus, High Noon, Imitator Aquatic, Makaze, Milestone, Mojave 70 EG, Imazuron, MSM 60, Panoramic 2SL, Picloram 22K, Triumph 22K, Tordon 22K, Piper EZ, Outrider |
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

**Windows** — `RushCountyPesticideLabels-0.2.2-win-x64.msix` is signed with a self-signed certificate, so each PC must trust that certificate once before the package will install:

1. As an administrator, import `RushCountyPesticideLabels.cer` into **Local Machine → Trusted People**.
2. Double-click the `.msix` and choose **Install**. On a PC that already has an earlier version, this updates it and keeps the saved labels and edits.

**Android** — copy `RushCountyPesticideLabels-0.2.2-android.apk` to the tablet or phone, open it, and allow installing from that source. On a device that already has an earlier version, this updates it and keeps the saved labels and edits. It supports Android 7 and later on 64-bit ARM and x86 tablets.

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

- `src/PesticideLabels.Core` — PPLS and CDMS clients, the county product list, on-device storage of details and PDFs, and the removal of unused PDFs.
- `src/PesticideLabels.App` — the MAUI app (Windows and Android).
- `tests/PesticideLabels.Tests` — unit tests for the core library.
