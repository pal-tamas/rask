namespace Rask.Workbooks;

/// <summary>The namespaces a workbook is written in: Excel's usual (transitional) ones, and ISO strict's.</summary>
internal static class Ooxml
{
#pragma warning disable S5332 // a namespace name, compared as text and never fetched; the standard spells it with http
    private const string StrictMain = "http://purl.oclc.org/ooxml/spreadsheetml/main";
    private const string StrictOffice = "http://purl.oclc.org/ooxml/officeDocument/relationships";
#pragma warning restore S5332

    public static bool IsMain(string ns) => ns is WorksheetWriter.Main or StrictMain;

    public static bool IsOffice(string ns) => ns is WorksheetWriter.Office or StrictOffice;
}
