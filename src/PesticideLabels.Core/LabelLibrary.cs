using System.Text.Json;

namespace PesticideLabels.Core;

/// <summary>Result of looking up a product: live from EPA, or the copy saved on this device.</summary>
public sealed record ProductLookup(PplsProduct? Product, bool FromSavedCopy, DateTimeOffset? FetchedAt, string? Error);

/// <summary>
/// Result of looking for a product on CDMS. <see cref="Match"/> is the CDMS product the
/// <see cref="Sheets"/> (Safety Data Sheets) and <see cref="Labels"/> belong to. When it is null, <see cref="Candidates"/> lists the CDMS products
/// with a similar name for staff to choose from.
/// </summary>
public sealed record SdsLookup(
    CdmsProduct? Match,
    IReadOnlyList<CdmsDocument> Sheets,
    IReadOnlyList<CdmsProduct> Candidates,
    bool FromSavedCopy,
    DateTimeOffset? FetchedAt,
    string? Error)
{
    /// <summary>The manufacturer's labels and supplemental label documents, in CDMS's order.</summary>
    public IReadOnlyList<CdmsDocument> Labels { get; init; } = [];
}

/// <summary>What <see cref="LabelLibrary.RemoveUnusedDocuments"/> deleted.</summary>
public sealed record CleanupSummary(int Removed, long BytesFreed);

public sealed record DownloadProgress(int Done, int Total, string Current);

public sealed record DownloadSummary(int Saved, int AlreadySaved, IReadOnlyList<string> Failed);

/// <summary>
/// The county product list plus everything saved on the device for offline use:
/// product details from PPLS, document lists from CDMS, and the PDFs that have been opened or downloaded.
/// </summary>
public sealed class LabelLibrary
{
    private readonly string _root;
    private readonly PplsClient _ppls;
    private readonly CdmsClient _cdms;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _productsLock = new(1, 1);

    public LabelLibrary(string rootDirectory, PplsClient ppls, HttpClient http)
    {
        _root = rootDirectory;
        _ppls = ppls;
        _cdms = new CdmsClient(http);
        _http = http;
        Directory.CreateDirectory(DetailsDir);
        Directory.CreateDirectory(LabelsDir);
        Directory.CreateDirectory(CdmsDir);
        Directory.CreateDirectory(SdsDir);
    }

    private string ProductsFile => Path.Combine(_root, "products.json");
    private string DetailsDir => Path.Combine(_root, "ppls");
    private string LabelsDir => Path.Combine(_root, "labels");
    private string CdmsDir => Path.Combine(_root, "cdms");
    private string SdsDir => Path.Combine(_root, "sds");

    // ---- County product list -------------------------------------------------------------

    /// <summary>The county list with any edits made on this device. Falls back to the built-in list.</summary>
    public async Task<List<CountyProduct>> LoadProductsAsync(CancellationToken ct = default)
    {
        await _productsLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(ProductsFile))
            {
                try
                {
                    await using var stream = File.OpenRead(ProductsFile);
                    var saved = await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.ListCountyProduct, ct).ConfigureAwait(false);
                    if (saved is { Count: > 0 })
                        return saved;
                }
                catch (JsonException)
                {
                    // A damaged file must not lock staff out of the list; start again from the built-in one.
                }
            }
            return SeedProducts.Create();
        }
        finally
        {
            _productsLock.Release();
        }
    }

    public async Task SaveProductsAsync(List<CountyProduct> products, CancellationToken ct = default)
    {
        await _productsLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var temp = ProductsFile + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, products, CoreJson.Default.ListCountyProduct, ct).ConfigureAwait(false);
            File.Move(temp, ProductsFile, overwrite: true);
        }
        finally
        {
            _productsLock.Release();
        }
    }

    // ---- Product details -----------------------------------------------------------------

    /// <summary>
    /// Gets product details from EPA and saves them. If EPA cannot be reached, returns the
    /// copy saved on this device (if any) together with the error.
    /// </summary>
    public async Task<ProductLookup> GetProductAsync(string regNo, CancellationToken ct = default)
    {
        if (!PplsClient.TryNormalizeRegNo(regNo, out var normalized))
            return new ProductLookup(null, false, null, $"\"{regNo}\" is not an EPA registration number.");

        var file = Path.Combine(DetailsDir, normalized + ".json");
        try
        {
            var product = await _ppls.GetProductAsync(normalized, ct).ConfigureAwait(false);
            if (product is null)
                return new ProductLookup(null, false, DateTimeOffset.Now, $"EPA has no product registered as {normalized}.");

            var cached = new CachedProduct { FetchedAt = DateTimeOffset.Now, Product = product };
            var temp = file + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, cached, CoreJson.Default.CachedProduct, ct).ConfigureAwait(false);
            File.Move(temp, file, overwrite: true);
            return new ProductLookup(product, false, cached.FetchedAt, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException && !ct.IsCancellationRequested)
        {
            var saved = await ReadSavedAsync(file).ConfigureAwait(false);
            return new ProductLookup(saved?.Product, saved?.Product is not null, saved?.FetchedAt, Describe(ex));
        }
    }

    /// <summary>The details saved on this device for a registration number, without contacting EPA.</summary>
    public async Task<ProductLookup> GetSavedProductAsync(string regNo)
    {
        if (!PplsClient.TryNormalizeRegNo(regNo, out var normalized))
            return new ProductLookup(null, false, null, null);
        var saved = await ReadSavedAsync(Path.Combine(DetailsDir, normalized + ".json")).ConfigureAwait(false);
        return new ProductLookup(saved?.Product, saved?.Product is not null, saved?.FetchedAt, null);
    }

    private static async Task<CachedProduct?> ReadSavedAsync(string file)
    {
        if (!File.Exists(file))
            return null;
        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.CachedProduct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    // ---- Label PDFs ----------------------------------------------------------------------

    public string LabelPath(string fileName)
    {
        if (!PplsClient.IsValidLabelFileName(fileName))
            throw new ArgumentException("Not a PPLS label file name.", nameof(fileName));
        return Path.Combine(LabelsDir, fileName);
    }

    public bool IsLabelSaved(string fileName) =>
        PplsClient.IsValidLabelFileName(fileName) && File.Exists(LabelPath(fileName));

    /// <summary>Returns the path of the saved PDF, downloading it from EPA first if needed.</summary>
    public Task<string> EnsureLabelAsync(string fileName, CancellationToken ct = default) =>
        EnsurePdfAsync(LabelPath(fileName), PplsClient.LabelPdfUri(fileName), "EPA returned something that is not a PDF for this label.", ct);

    private async Task<string> EnsurePdfAsync(string path, Uri uri, string notPdfMessage, CancellationToken ct)
    {
        if (File.Exists(path))
        {
            MarkUsed(path);
            return path;
        }

        var temp = path + ".part";
        try
        {
            using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(temp);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
            }

            if (!await LooksLikePdfAsync(temp, ct).ConfigureAwait(false))
                throw new InvalidDataException(notPdfMessage);

            File.Move(temp, path, overwrite: true);
            return path;
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static async Task<bool> LooksLikePdfAsync(string path, CancellationToken ct)
    {
        var header = new byte[5];
        await using var stream = File.OpenRead(path);
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        return read == 5 && header.AsSpan().SequenceEqual("%PDF-"u8);
    }

    /// <summary>
    /// Saves the newest label of every product that has a registration number,
    /// so the labels open without a connection.
    /// </summary>
    public async Task<DownloadSummary> DownloadAllLatestAsync(
        IReadOnlyList<CountyProduct> products, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var targets = products.Where(p => p.HasRegNo).ToList();
        int saved = 0, already = 0, done = 0;
        var failed = new List<string>();

        foreach (var product in targets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new DownloadProgress(done, targets.Count, product.Name));
            try
            {
                var lookup = await GetProductAsync(product.EpaRegNo!, ct).ConfigureAwait(false);
                var newest = lookup.Product?.LabelsNewestFirst.FirstOrDefault();
                if (newest is null)
                {
                    failed.Add($"{product.Name}: {lookup.Error ?? "EPA lists no label PDF"}");
                }
                else if (IsLabelSaved(newest.FileName))
                {
                    // Asking for the download counts as use, so the label is kept for another week.
                    MarkUsed(LabelPath(newest.FileName));
                    already++;
                }
                else
                {
                    await EnsureLabelAsync(newest.FileName, ct).ConfigureAwait(false);
                    saved++;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                failed.Add($"{product.Name}: {Describe(ex)}");
            }
            done++;
        }

        progress?.Report(new DownloadProgress(done, targets.Count, ""));
        return new DownloadSummary(saved, already, failed);
    }

    // ---- Removing documents nobody has opened ---------------------------------------------

    /// <summary>How long a saved document is kept on a tablet or phone after it was last opened.</summary>
    public static readonly TimeSpan UnusedDocumentLifetime = TimeSpan.FromDays(7);

    /// <summary>A saved PDF's modified time is the last time it was downloaded or opened.</summary>
    private static void MarkUsed(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The document still opens; at worst it is removed early and downloaded again.
        }
    }

    /// <summary>
    /// Deletes the saved labels and Safety Data Sheets that were last downloaded or opened more
    /// than <paramref name="unusedFor"/> ago. The product list, the EPA details and the CDMS
    /// document lists are kept, so a removed document only needs a connection to come back.
    /// </summary>
    public CleanupSummary RemoveUnusedDocuments(TimeSpan unusedFor, DateTime? nowUtc = null)
    {
        var cutoff = (nowUtc ?? DateTime.UtcNow) - unusedFor;
        var removed = 0;
        long freed = 0;
        foreach (var folder in (string[])[LabelsDir, SdsDir])
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc >= cutoff)
                        continue;
                    var size = info.Length;
                    info.Delete();
                    // Anything else this old is a download that was cut off.
                    if (info.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        removed++;
                        freed += size;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use or already gone; it is looked at again next time.
                }
            }
        }
        return new CleanupSummary(removed, freed);
    }

    // ---- Labels and Safety Data Sheets from CDMS -----------------------------------------

    public const string Cdms = "CDMS";

    /// <summary>
    /// Finds the product's labels and Safety Data Sheets on CDMS and saves the list. A product already
    /// matched (<see cref="CountyProduct.CdmsProductId"/>) is looked up directly. Otherwise CDMS is
    /// searched by name: one US product with the same EPA Reg. No. is taken as the match, and
    /// anything less certain is returned as candidates for staff to choose from.
    /// If CDMS cannot be reached, the saved list (if any) is returned together with the error.
    /// </summary>
    public async Task<SdsLookup> FindSdsAsync(CountyProduct product, CancellationToken ct = default)
    {
        try
        {
            CdmsProduct? match = null;
            if (product.CdmsProductId is null)
            {
                IReadOnlyList<CdmsProduct> hits = [];
                foreach (var term in CdmsClient.SearchTerms(product))
                {
                    hits = await _cdms.SearchAsync(term, ct).ConfigureAwait(false);
                    if (hits.Count > 0)
                        break;
                }
                match = CdmsClient.MatchByRegNo(hits, product.HasRegNo ? product.EpaRegNo : null);
                if (match is null)
                    return new SdsLookup(null, [], hits, false, DateTimeOffset.Now, null);
            }

            var id = match?.Id ?? product.CdmsProductId!.Value;
            var file = SdsListFile(id);
            // The document list does not repeat the product's name, so keep the one saved when it was matched.
            match ??= (await ReadSavedSdsAsync(file).ConfigureAwait(false))?.Product;
            var documents = await _cdms.GetDocumentsAsync(id, ct).ConfigureAwait(false);
            var cached = new CachedSds
            {
                FetchedAt = DateTimeOffset.Now,
                Product = match ?? new CdmsProduct { Id = id },
                Sheets = [.. documents.Where(d => d.IsSafetyDataSheet)],
                Labels = [.. documents.Where(d => d.IsLabel)],
            };
            await WriteSdsAsync(file, cached, ct).ConfigureAwait(false);
            return new SdsLookup(cached.Product, cached.Sheets, [], false, cached.FetchedAt, null) { Labels = cached.Labels };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException && !ct.IsCancellationRequested)
        {
            var saved = product.CdmsProductId is { } id ? await ReadSavedSdsAsync(SdsListFile(id)).ConfigureAwait(false) : null;
            return new SdsLookup(saved?.Product, saved?.Sheets ?? [], [], saved is not null, saved?.FetchedAt, Describe(ex, Cdms))
            {
                Labels = saved?.Labels ?? [],
            };
        }
    }

    /// <summary>The label and Safety Data Sheet list saved on this device for a matched product, without contacting CDMS.</summary>
    public async Task<SdsLookup?> GetSavedSdsAsync(CountyProduct product)
    {
        if (product.CdmsProductId is not { } id || await ReadSavedSdsAsync(SdsListFile(id)).ConfigureAwait(false) is not { } saved)
            return null;
        return new SdsLookup(saved.Product, saved.Sheets, [], true, saved.FetchedAt, null) { Labels = saved.Labels };
    }

    /// <summary>
    /// Saves the name and maker of the CDMS product staff chose, so they can be shown again
    /// when CDMS cannot be reached.
    /// </summary>
    public async Task RememberSdsMatchAsync(CdmsProduct match, CancellationToken ct = default)
    {
        var file = SdsListFile(match.Id);
        var cached = await ReadSavedSdsAsync(file).ConfigureAwait(false) ?? new CachedSds { FetchedAt = DateTimeOffset.Now };
        cached.Product = match;
        await WriteSdsAsync(file, cached, ct).ConfigureAwait(false);
    }

    private string SdsListFile(int cdmsProductId) => Path.Combine(CdmsDir, cdmsProductId + ".json");

    private static async Task WriteSdsAsync(string file, CachedSds cached, CancellationToken ct)
    {
        var temp = file + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, cached, CoreJson.Default.CachedSds, ct).ConfigureAwait(false);
        File.Move(temp, file, overwrite: true);
    }

    private static async Task<CachedSds?> ReadSavedSdsAsync(string file)
    {
        if (!File.Exists(file))
            return null;
        try
        {
            await using var stream = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync(stream, CoreJson.Default.CachedSds).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public string SdsPath(string fileName)
    {
        if (!CdmsClient.IsValidFileName(fileName))
            throw new ArgumentException("Not a CDMS document file name.", nameof(fileName));
        return Path.Combine(SdsDir, fileName);
    }

    public bool IsSdsSaved(string fileName) => CdmsClient.IsValidFileName(fileName) && File.Exists(SdsPath(fileName));

    /// <summary>
    /// Returns the path of the saved CDMS document (a Safety Data Sheet or a label),
    /// downloading it from CDMS first if needed.
    /// </summary>
    public Task<string> EnsureSdsAsync(string fileName, CancellationToken ct = default) =>
        EnsurePdfAsync(SdsPath(fileName), CdmsClient.DocumentUri(fileName), "CDMS returned something that is not a PDF for this document.", ct);

    /// <summary>A plain-language reason for a failed request to <paramref name="source"/> (EPA or CDMS).</summary>
    public static string Describe(Exception ex, string source = "EPA") => ex switch
    {
        TaskCanceledException => $"{source}'s server took too long to answer.",
        HttpRequestException { StatusCode: { } code } => $"{source}'s server answered with an error ({(int)code}).",
        HttpRequestException => $"Could not reach {source}. Check the internet connection.",
        JsonException => $"{source}'s answer could not be read.",
        _ => ex.Message,
    };
}
