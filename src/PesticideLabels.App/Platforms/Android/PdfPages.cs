using System.Text;
using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;

namespace PesticideLabels.App;

/// <summary>Draws the pages of a PDF to images and writes a small web page that shows them in order.</summary>
public static class PdfPages
{
    private const int PageWidthPixels = 1600;

    /// <summary>Returns the path of the web page, rendering the PDF first unless that was already done.</summary>
    public static string Render(string pdfPath, string outputFolder, IProgress<(int Page, int Total)>? progress = null)
    {
        var index = System.IO.Path.Combine(outputFolder, "index.html");
        if (File.Exists(index))
            return index;

        // A folder without index.html is a render that was interrupted; start it again.
        if (Directory.Exists(outputFolder))
            Directory.Delete(outputFolder, recursive: true);
        Directory.CreateDirectory(outputFolder);

        using var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(pdfPath), ParcelFileMode.ReadOnly)
            ?? throw new IOException("The saved label could not be opened.");
        using var renderer = new PdfRenderer(descriptor);

        var html = new StringBuilder(
            "<!doctype html><html><head><meta charset=\"utf-8\">" +
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=8, user-scalable=yes\">" +
            "<style>body{margin:0;background:#555}img{display:block;width:100%;height:auto;margin:0 0 8px 0;background:#fff}</style>" +
            "</head><body>");

        var total = renderer.PageCount;
        for (var i = 0; i < total; i++)
        {
            progress?.Report((i + 1, total));
            using var page = renderer.OpenPage(i) ?? throw new IOException($"Page {i + 1} could not be read.");
            var height = Math.Max(1, (int)Math.Round((double)PageWidthPixels * page.Height / Math.Max(1, page.Width)));
            using var bitmap = Bitmap.CreateBitmap(PageWidthPixels, height, Bitmap.Config.Argb8888!)
                ?? throw new IOException("Not enough memory to draw the label.");
            bitmap.EraseColor(Android.Graphics.Color.White);
            page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);
            page.Close();

            var name = $"page{i + 1:000}.jpg";
            using (var stream = File.Create(System.IO.Path.Combine(outputFolder, name)))
                bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 88, stream);
            bitmap.Recycle();

            html.Append("<img src=\"").Append(name).Append("\" width=\"").Append(PageWidthPixels)
                .Append("\" height=\"").Append(height).Append("\" alt=\"Label page ").Append(i + 1).Append("\">");
        }

        html.Append("</body></html>");
        File.WriteAllText(index, html.ToString());
        return index;
    }
}
