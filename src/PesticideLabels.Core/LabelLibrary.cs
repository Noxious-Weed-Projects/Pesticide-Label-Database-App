using System.Text.Json;

namespace PesticideLabels.Core;

/// <summary>Result of looking up a product: live from EPA, or the copy saved on this device.</summary>
public sealed record ProductLookup(PplsProduct? Product, bool FromSavedCopy, DateTimeOffset? FetchedAt, string? Error);

public sealed record DownloadProgress(int Done, int Total, string Current);

public sealed record DownloadSummary(int Saved, int AlreadySaved, IReadOnlyList<string> Failed);

/// <summary>
/// The county product list plus everything saved on the device for offline use:
/// product details from PPLS and the label PDFs that have been opened or downloaded.
/// </summary>
public sealed class LabelLibrary
{
    private readonly string _root;
    private readonly PplsClient _ppls;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _productsLock = new(1, 1);

    public LabelLibrary(string rootDirectory, PplsClient ppls, HttpClient http)
    {
        _root = rootDirectory;
        _ppls = ppls;
        _http = http;
        Directory.CreateDirectory(DetailsDir);
        Directory.CreateDirectory(LabelsDir);
    }

    private string ProductsFile => Path.Combine(_root, "products.json");
    private string DetailsDir => Path.Combine(_root, "ppls");
    private string LabelsDir => Path.Combine(_root, "labels");

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
    public async Task<string> EnsureLabelAsync(string fileName, CancellationToken ct = default)
    {
        var path = LabelPath(fileName);
        if (File.Exists(path))
            return path;

        var temp = path + ".part";
        try
        {
            using (var response = await _http.GetAsync(PplsClient.LabelPdfUri(fileName), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var target = File.Create(temp);
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
            }

            if (!await LooksLikePdfAsync(temp, ct).ConfigureAwait(false))
                throw new InvalidDataException("EPA returned something that is not a PDF for this label.");

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

    public static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "EPA's server took too long to answer.",
        HttpRequestException { StatusCode: { } code } => $"EPA's server answered with an error ({(int)code}).",
        HttpRequestException => "Could not reach EPA. Check the internet connection.",
        JsonException => "EPA's answer could not be read.",
        _ => ex.Message,
    };
}
