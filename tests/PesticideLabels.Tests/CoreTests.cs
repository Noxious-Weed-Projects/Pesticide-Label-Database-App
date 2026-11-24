using System.Net;
using System.Text;
using PesticideLabels.Core;

namespace PesticideLabels.Tests;

/// <summary>Answers requests from a table of URL → response, and records what was asked.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public Dictionary<string, Func<HttpResponseMessage>> Routes { get; } = [];
    public List<string> Requests { get; } = [];
    public bool Offline { get; set; }

    public void Json(string url, string body) =>
        Routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public void Bytes(string url, byte[] body) =>
        Routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add(url);
        if (Offline)
            throw new HttpRequestException("No connection");
        return Task.FromResult(Routes.TryGetValue(url, out var make) ? make() : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public sealed class CoreTests : IDisposable
{
    private const string Empty = """{"items":[]}""";
    private static readonly string Milestone = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "milestone.json"));
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nfake label\n%%EOF");

    private readonly string _dir = Directory.CreateTempSubdirectory("labels-test").FullName;
    private readonly FakeHandler _handler = new();
    private readonly LabelLibrary _library;
    private readonly PplsClient _client;

    public CoreTests()
    {
        var http = new HttpClient(_handler);
        _client = new PplsClient(http);
        _library = new LabelLibrary(_dir, _client, http);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Seed_list_has_the_34_county_products_with_unique_ids()
    {
        var products = SeedProducts.Create();
        Assert.Equal(34, products.Count);
        Assert.Equal(34, products.Select(p => p.Id).Distinct().Count());
        Assert.All(products.Where(p => p.EpaRegNo is not null), p => Assert.True(PplsClient.TryNormalizeRegNo(p.EpaRegNo, out _), p.Name));
        Assert.All(products, p => Assert.False(p.Confirmed));
        Assert.Equal(9, products.Count(p => p.Source == LabelSource.NoEpaLabel));
    }

    [Theory]
    [InlineData("Alligare 2,4-D", "81927-38")]
    [InlineData("AquaNeat", "228-365")]
    [InlineData("Diablo", "228-379")]
    [InlineData("Drexel Diuron 4L", "19713-36")]
    [InlineData("Loveland MAD DOG", "34704-889")]
    [InlineData("Loveland Rifle", "34704-861")]
    [InlineData("Cornbelt 4 Lb. Amine", "11773-2")]
    public void Seed_list_has_the_numbers_from_the_county_list(string name, string? regNo) =>
        Assert.Equal(regNo, SeedProducts.Create().Single(p => p.Name == name).EpaRegNo);

    [Fact]
    public async Task A_list_saved_by_an_earlier_version_gains_the_new_product_and_numbers()
    {
        // What v0.2.0 would have saved: no Cornbelt, and no numbers for these products.
        var old = SeedProducts.Create();
        old.RemoveAll(p => p.Name == "Cornbelt 4 Lb. Amine");
        foreach (var name in new[] { "Alligare 2,4-D", "AquaNeat", "Drexel Diuron 4L", "Loveland MAD DOG", "Loveland Rifle" })
            old.Single(p => p.Name == name).EpaRegNo = null;
        // Staff edits made on the device must survive.
        old.Single(p => p.Name == "Loveland MAD DOG").EpaRegNo = "34704-890";
        old.Single(p => p.Name == "Corteva Milestone").Confirmed = true;
        await _library.SaveProductsAsync(old);

        var loaded = await _library.LoadProductsAsync();

        Assert.Equal(34, loaded.Count);
        Assert.Equal("11773-2", loaded.Single(p => p.Name == "Cornbelt 4 Lb. Amine").EpaRegNo);
        Assert.Equal("81927-38", loaded.Single(p => p.Name == "Alligare 2,4-D").EpaRegNo);
        Assert.Equal("19713-36", loaded.Single(p => p.Name == "Drexel Diuron 4L").EpaRegNo);
        Assert.Equal("34704-861", loaded.Single(p => p.Name == "Loveland Rifle").EpaRegNo);
        Assert.Equal("34704-890", loaded.Single(p => p.Name == "Loveland MAD DOG").EpaRegNo);
        Assert.True(loaded.Single(p => p.Name == "Corteva Milestone").Confirmed);
        Assert.False(SeedProducts.ApplyListUpdates(loaded));
    }

    [Fact]
    public async Task A_list_saved_by_v0_2_1_gains_the_Cornbelt_number()
    {
        // v0.2.1 had Cornbelt on the list with no number.
        var old = SeedProducts.Create();
        old.Single(p => p.Name == "Cornbelt 4 Lb. Amine").EpaRegNo = null;
        await _library.SaveProductsAsync(old);

        var loaded = await _library.LoadProductsAsync();

        Assert.Equal(34, loaded.Count);
        var cornbelt = loaded.Single(p => p.Name == "Cornbelt 4 Lb. Amine");
        Assert.Equal("11773-2", cornbelt.EpaRegNo);
        Assert.False(cornbelt.Confirmed);
    }

    [Theory]
    [InlineData("62719-519", true)]
    [InlineData(" 66222-141-81927 ", true)]
    [InlineData("KS120001", false)]
    [InlineData("62719", false)]
    [InlineData("../62719-519", false)]
    [InlineData("", false)]
    public void Registration_numbers_are_validated(string text, bool valid) =>
        Assert.Equal(valid, PplsClient.TryNormalizeRegNo(text, out _));

    [Theory]
    [InlineData("062719-00519-20210204.pdf", true)]
    [InlineData("../secret.pdf", false)]
    [InlineData("a/b.pdf", false)]
    [InlineData("label.exe", false)]
    public void Label_file_names_are_validated(string name, bool valid) =>
        Assert.Equal(valid, PplsClient.IsValidLabelFileName(name));

    [Fact]
    public void Search_url_escapes_the_product_name() =>
        Assert.Equal(
            PplsClient.ApiBase + "ProductSearch/partialprodsearch/v2/riname/2%2C4-D%20LV%206",
            PplsClient.SearchUri(" 2,4-D LV 6 ").AbsoluteUri);

    [Fact]
    public async Task Product_details_are_read_from_a_real_ppls_answer()
    {
        _handler.Json(PplsClient.ProductUri("62719-519").AbsoluteUri, Milestone);

        var product = await _client.GetProductAsync("62719-519");

        Assert.NotNull(product);
        Assert.Equal("MILESTONE", product.ProductName);
        Assert.Equal("Caution", product.SignalWord);
        Assert.False(product.IsRestrictedUse);
        Assert.Equal("CORTEVA AGRISCIENCE, LLC", product.Companies[0].Name);
        Assert.Equal(40.6, product.ActiveIngredients[0].Percent);
        var labels = product.LabelsNewestFirst;
        Assert.Equal("062719-00519-20210204.pdf", labels[0].FileName);
        Assert.Equal(new DateTime(2021, 2, 4), labels[0].AcceptedDate);
        Assert.Equal(labels.OrderByDescending(l => l.AcceptedDate).Select(l => l.FileName), labels.Select(l => l.FileName));
    }

    [Fact]
    public async Task Distributor_number_falls_back_to_the_parent_registration()
    {
        _handler.Json(PplsClient.ProductUri("62719-519-81927").AbsoluteUri, Empty);
        _handler.Json(PplsClient.ProductUri("62719-519").AbsoluteUri, Milestone);

        var product = await _client.GetProductAsync("62719-519-81927");

        Assert.Equal("62719-519", product?.EpaRegNo);
    }

    [Fact]
    public async Task Search_puts_active_federal_products_first_and_drops_duplicates()
    {
        _handler.Json(PplsClient.SearchUri("milestone").AbsoluteUri, """
            {"items":[
              {"eparegno":"GA090005","productname":"MILESTONE VM","product_status":"Inactive"},
              {"eparegno":"AS190001","productname":"MILESTONE","product_status":"Active"},
              {"eparegno":"62719-519","productname":"MILESTONE","product_status":"Active"},
              {"eparegno":"62719-519","productname":"Milestone ","product_status":"Active"}
            ]}
            """);

        var hits = await _client.SearchByNameAsync("milestone");

        Assert.Equal(["62719-519", "AS190001", "GA090005"], hits.Select(h => h.EpaRegNo));
    }

    [Fact]
    public async Task Details_are_saved_and_reused_when_epa_cannot_be_reached()
    {
        _handler.Json(PplsClient.ProductUri("62719-519").AbsoluteUri, Milestone);
        var live = await _library.GetProductAsync("62719-519");
        Assert.False(live.FromSavedCopy);
        Assert.Null(live.Error);

        _handler.Offline = true;
        var offline = await _library.GetProductAsync("62719-519");

        Assert.True(offline.FromSavedCopy);
        Assert.Equal("MILESTONE", offline.Product?.ProductName);
        Assert.NotNull(offline.Error);
        Assert.NotNull(offline.FetchedAt);
    }

    [Fact]
    public async Task Offline_with_nothing_saved_reports_an_error_and_no_product()
    {
        _handler.Offline = true;
        var lookup = await _library.GetProductAsync("62719-519");
        Assert.Null(lookup.Product);
        Assert.False(lookup.FromSavedCopy);
        Assert.NotNull(lookup.Error);
    }

    [Fact]
    public async Task Label_is_downloaded_once_and_then_opened_from_the_device()
    {
        const string file = "062719-00519-20210204.pdf";
        _handler.Bytes(PplsClient.LabelPdfUri(file).AbsoluteUri, Pdf);

        var path = await _library.EnsureLabelAsync(file);
        _handler.Offline = true;
        var again = await _library.EnsureLabelAsync(file);

        Assert.Equal(path, again);
        Assert.Equal(Pdf, await File.ReadAllBytesAsync(path));
        Assert.True(_library.IsLabelSaved(file));
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task A_download_that_is_not_a_pdf_is_rejected_and_not_kept()
    {
        const string file = "062719-00519-20210204.pdf";
        _handler.Bytes(PplsClient.LabelPdfUri(file).AbsoluteUri, Encoding.ASCII.GetBytes("<html>Sorry</html>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => _library.EnsureLabelAsync(file));

        Assert.False(_library.IsLabelSaved(file));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "labels")));
    }

    [Fact]
    public async Task Download_all_saves_the_newest_label_and_reports_failures()
    {
        _handler.Json(PplsClient.ProductUri("62719-519").AbsoluteUri, Milestone);
        _handler.Json(PplsClient.ProductUri("1-1").AbsoluteUri, Empty);
        _handler.Bytes(PplsClient.LabelPdfUri("062719-00519-20210204.pdf").AbsoluteUri, Pdf);
        List<CountyProduct> products =
        [
            new() { Name = "Milestone", EpaRegNo = "62719-519" },
            new() { Name = "Unknown", EpaRegNo = "1-1" },
            new() { Name = "No number yet" },
            new() { Name = "Dye", Source = LabelSource.NoEpaLabel },
        ];

        var first = await _library.DownloadAllLatestAsync(products);
        var second = await _library.DownloadAllLatestAsync(products);

        Assert.Equal(1, first.Saved);
        Assert.Single(first.Failed);
        Assert.StartsWith("Unknown:", first.Failed[0]);
        Assert.Equal(0, second.Saved);
        Assert.Equal(1, second.AlreadySaved);
    }

    // ---- Safety Data Sheets (CDMS) ----

    private const string CdmsMilestoneSearch = """
        {"SiteUrl":"http://www.cdms.net/","LabelFolder":"ldat/","Lst":[
        {"Id":8113,"Name":"Milestone® VM Specialty Herbicide ","ManId":525,"EPA":"62719-537","Manufacturer":"Corteva Agriscience United States","CommonName":"AMINOPYRALID","IsUs":true,"IsCanada":false},
        {"Id":7885,"Name":"Milestone® Specialty Herbicide","ManId":525,"EPA":"62719-519","Manufacturer":"Corteva Agriscience United States","CommonName":"AMINOPYRALID","IsUs":true,"IsCanada":false}]}
        """;

    private const string CdmsMilestoneDocuments = """
        {"ProductId":7885,"SiteUrl":"https://www.cdms.net/","LabelFolder":"ldat/","Lst":[
        {"FileName":"ld77N057.pdf","Description":"CD02-879-023","DocType":"Specimen Label","DocId":138511,"LanguageCodes":["en"]},
        {"FileName":"mp77N004.pdf","Description":"CD03-879-020 (issue date 6-29-22 version 1.0)","DocType":"SDS","DocId":140001,"LanguageCodes":["en"]},
        {"FileName":"../evil.pdf","Description":"bad name","DocType":"SDS","DocId":1,"LanguageCodes":[]}]}
        """;

    [Fact]
    public void Cdms_search_tries_the_name_without_the_manufacturer() =>
        Assert.Equal(["Corteva Agriscience Piper EZ", "Agriscience Piper EZ", "Piper EZ", "PIPER"],
            CdmsClient.SearchTerms(new CountyProduct { Name = "Corteva Agriscience Piper EZ", SearchHint = "PIPER" }));

    [Fact]
    public async Task Sds_is_matched_by_epa_number_and_only_safety_data_sheets_are_listed()
    {
        _handler.Json(CdmsClient.SearchUri("Milestone").AbsoluteUri, CdmsMilestoneSearch);
        _handler.Json(CdmsClient.DocumentListUri(7885).AbsoluteUri, CdmsMilestoneDocuments);

        var lookup = await _library.FindSdsAsync(new CountyProduct { Name = "Milestone", EpaRegNo = "62719-519" });

        Assert.Null(lookup.Error);
        Assert.Equal(7885, lookup.Match?.Id);
        Assert.Equal("mp77N004.pdf", Assert.Single(lookup.Sheets).FileName);
    }

    [Fact]
    public async Task Sds_without_a_matching_epa_number_offers_the_products_to_choose_from()
    {
        _handler.Json(CdmsClient.SearchUri("Milestone").AbsoluteUri, CdmsMilestoneSearch);

        var lookup = await _library.FindSdsAsync(new CountyProduct { Name = "Milestone", EpaRegNo = "1-1" });

        Assert.Null(lookup.Match);
        Assert.Equal([8113, 7885], lookup.Candidates.Select(c => c.Id));
        Assert.Empty(lookup.Sheets);
    }

    [Fact]
    public async Task Sds_search_falls_back_to_the_name_without_the_manufacturer()
    {
        _handler.Json(CdmsClient.SearchUri("Corteva Milestone").AbsoluteUri, """{"Lst":[]}""");
        _handler.Json(CdmsClient.SearchUri("Milestone").AbsoluteUri, CdmsMilestoneSearch);
        _handler.Json(CdmsClient.DocumentListUri(7885).AbsoluteUri, CdmsMilestoneDocuments);

        var lookup = await _library.FindSdsAsync(new CountyProduct { Name = "Corteva Milestone", EpaRegNo = "62719-519" });

        Assert.Equal(7885, lookup.Match?.Id);
    }

    [Fact]
    public async Task Sds_list_of_a_matched_product_is_reused_when_cdms_cannot_be_reached()
    {
        _handler.Json(CdmsClient.SearchUri("Milestone").AbsoluteUri, CdmsMilestoneSearch);
        _handler.Json(CdmsClient.DocumentListUri(7885).AbsoluteUri, CdmsMilestoneDocuments);
        var product = new CountyProduct { Name = "Milestone", EpaRegNo = "62719-519" };
        product.CdmsProductId = (await _library.FindSdsAsync(product)).Match!.Id;

        _handler.Offline = true;
        var offline = await _library.FindSdsAsync(product);
        var saved = await _library.GetSavedSdsAsync(product);

        Assert.True(offline.FromSavedCopy);
        Assert.Contains("CDMS", offline.Error);
        Assert.Equal("Milestone® Specialty Herbicide", offline.Match?.Name);
        Assert.Single(offline.Sheets);
        Assert.Single(saved!.Sheets);
    }

    [Fact]
    public async Task Sds_offline_with_no_match_reports_an_error()
    {
        _handler.Offline = true;
        var lookup = await _library.FindSdsAsync(new CountyProduct { Name = "Milestone", EpaRegNo = "62719-519" });
        Assert.Null(lookup.Match);
        Assert.False(lookup.FromSavedCopy);
        Assert.NotNull(lookup.Error);
    }

    [Fact]
    public async Task A_product_chosen_by_hand_keeps_its_name_after_the_sheets_are_fetched()
    {
        _handler.Json(CdmsClient.DocumentListUri(7885).AbsoluteUri, CdmsMilestoneDocuments);
        await _library.RememberSdsMatchAsync(new CdmsProduct { Id = 7885, Name = "Milestone® Specialty Herbicide", Manufacturer = "Corteva" });

        var lookup = await _library.FindSdsAsync(new CountyProduct { Name = "Milestone", CdmsProductId = 7885 });

        Assert.Equal("Milestone® Specialty Herbicide", lookup.Match?.Name);
        Assert.Single(lookup.Sheets);
        Assert.DoesNotContain(_handler.Requests, r => r.Contains("ProductList"));
    }

    [Fact]
    public async Task Sds_pdf_is_downloaded_once_and_a_non_pdf_is_rejected()
    {
        _handler.Bytes(CdmsClient.DocumentUri("mp77N004.pdf").AbsoluteUri, Pdf);
        _handler.Bytes(CdmsClient.DocumentUri("mpBAD.pdf").AbsoluteUri, Encoding.ASCII.GetBytes("<html>Sorry</html>"));

        var path = await _library.EnsureSdsAsync("mp77N004.pdf");
        await _library.EnsureSdsAsync("mp77N004.pdf");
        await Assert.ThrowsAsync<InvalidDataException>(() => _library.EnsureSdsAsync("mpBAD.pdf"));

        Assert.Equal(Pdf, await File.ReadAllBytesAsync(path));
        Assert.True(_library.IsSdsSaved("mp77N004.pdf"));
        Assert.False(_library.IsSdsSaved("mpBAD.pdf"));
        Assert.Equal(2, _handler.Requests.Count);
        Assert.Throws<ArgumentException>(() => _library.SdsPath("../evil.pdf"));
    }

    [Fact]
    public async Task Cdms_labels_are_listed_apart_from_safety_data_sheets_and_kept_for_offline()
    {
        const string documents = """
            {"ProductId":7885,"Lst":[
            {"FileName":"ld77N057.pdf","Description":"CD02-879-023","DocType":"Specimen Label","DocId":1,"LanguageCodes":["en"]},
            {"FileName":"ld77N011.pdf","Description":"R879-008","DocType":"2(ee) Recommendation","DocId":2,"LanguageCodes":["en"]},
            {"FileName":"mp77N004.pdf","Description":"SDS","DocType":"SDS","DocId":3,"LanguageCodes":["en"]},
            {"FileName":"../evil.pdf","Description":"bad name","DocType":"Specimen Label","DocId":4,"LanguageCodes":[]}]}
            """;
        _handler.Json(CdmsClient.SearchUri("Milestone").AbsoluteUri, CdmsMilestoneSearch);
        _handler.Json(CdmsClient.DocumentListUri(7885).AbsoluteUri, documents);
        var product = new CountyProduct { Name = "Milestone", EpaRegNo = "62719-519" };

        var live = await _library.FindSdsAsync(product);
        product.CdmsProductId = live.Match!.Id;
        _handler.Offline = true;
        var offline = await _library.FindSdsAsync(product);

        Assert.Equal(["ld77N057.pdf", "ld77N011.pdf"], live.Labels.Select(l => l.FileName));
        Assert.Equal([true, false], live.Labels.Select(l => l.IsMainLabel));
        Assert.Single(live.Sheets);
        Assert.Equal(2, offline.Labels.Count);
        Assert.Equal(2, (await _library.GetSavedSdsAsync(product))!.Labels.Count);
    }

    [Fact]
    public async Task Documents_not_opened_for_a_week_are_removed_and_the_rest_are_kept()
    {
        _handler.Bytes(PplsClient.LabelPdfUri("old-label.pdf").AbsoluteUri, Pdf);
        _handler.Bytes(PplsClient.LabelPdfUri("used-label.pdf").AbsoluteUri, Pdf);
        _handler.Bytes(CdmsClient.DocumentUri("old-sds.pdf").AbsoluteUri, Pdf);
        var oldLabel = await _library.EnsureLabelAsync("old-label.pdf");
        var usedLabel = await _library.EnsureLabelAsync("used-label.pdf");
        var oldSds = await _library.EnsureSdsAsync("old-sds.pdf");
        var cutOff = oldLabel + ".part";
        await File.WriteAllBytesAsync(cutOff, Pdf);
        await _library.SaveProductsAsync(await _library.LoadProductsAsync());
        var longAgo = DateTime.UtcNow.AddDays(-8);
        foreach (var file in (string[])[oldLabel, usedLabel, oldSds, cutOff, Path.Combine(_dir, "products.json")])
            File.SetLastWriteTimeUtc(file, longAgo);

        // Opening a saved document counts as use.
        await _library.EnsureLabelAsync("used-label.pdf");
        var summary = _library.RemoveUnusedDocuments(LabelLibrary.UnusedDocumentLifetime);

        Assert.Equal(new CleanupSummary(2, Pdf.Length * 2), summary);
        Assert.False(_library.IsLabelSaved("old-label.pdf"));
        Assert.False(_library.IsSdsSaved("old-sds.pdf"));
        Assert.False(File.Exists(cutOff));
        Assert.True(_library.IsLabelSaved("used-label.pdf"));
        Assert.True(File.Exists(Path.Combine(_dir, "products.json")));
        Assert.Equal(0, _library.RemoveUnusedDocuments(LabelLibrary.UnusedDocumentLifetime).Removed);
    }

    [Fact]
    public async Task Download_all_keeps_an_already_saved_label_for_another_week()
    {
        _handler.Json(PplsClient.ProductUri("62719-519").AbsoluteUri, Milestone);
        _handler.Bytes(PplsClient.LabelPdfUri("062719-00519-20210204.pdf").AbsoluteUri, Pdf);
        List<CountyProduct> products = [new() { Name = "Milestone", EpaRegNo = "62719-519" }];
        await _library.DownloadAllLatestAsync(products);
        File.SetLastWriteTimeUtc(_library.LabelPath("062719-00519-20210204.pdf"), DateTime.UtcNow.AddDays(-8));

        await _library.DownloadAllLatestAsync(products);

        Assert.Equal(0, _library.RemoveUnusedDocuments(LabelLibrary.UnusedDocumentLifetime).Removed);
    }

    [Fact]
    public async Task Edits_to_the_product_list_survive_a_restart()
    {
        var products = await _library.LoadProductsAsync();
        var rifle = products.Single(p => p.Name == "Loveland Rifle");
        rifle.EpaRegNo = "34704-861";
        rifle.Confirmed = true;
        await _library.SaveProductsAsync(products);

        var reloaded = await new LabelLibrary(_dir, _client, new HttpClient(_handler)).LoadProductsAsync();

        var saved = reloaded.Single(p => p.Name == "Loveland Rifle");
        Assert.Equal("34704-861", saved.EpaRegNo);
        Assert.True(saved.Confirmed);
        Assert.Equal(34, reloaded.Count);
    }

    [Fact]
    public async Task A_damaged_product_file_falls_back_to_the_built_in_list()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "products.json"), "{ not json");
        Assert.Equal(34, (await _library.LoadProductsAsync()).Count);
    }
}
