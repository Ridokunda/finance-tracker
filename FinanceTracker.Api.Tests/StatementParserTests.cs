using System.Text;
using FinanceTracker.Api.Services;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
namespace FinanceTracker.Api.Tests;
public class StatementParserTests
{
    [Theory]
    [InlineData(",", "\"Shop, Cape Town\"")]
    [InlineData(";", "Shop, Cape Town")]
    public void CsvHandlesQuotedCommasAndSemicolons(string delimiter, string description)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"Date{delimiter}Description{delimiter}Amount\n2026-09-01{delimiter}{description}{delimiter}-1200.00\n"));
        var row = Assert.Single(new StatementParser().Csv(stream, new()).Rows);
        Assert.Null(row.Error); Assert.Equal(-1200m, row.Amount); Assert.Equal("Shop, Cape Town", row.Description);
        Assert.Equal(new DateTime(2026, 9, 1), row.Date);
    }
    [Theory]
    [InlineData("1,2")][InlineData("1.234")][InlineData("12abc")][InlineData("0")]
    public void MalformedAmountsAreRejected(string amount) => Assert.False(StatementParser.TryAmount(amount, out _));
    [Fact]
    public void AmbiguousDatesRequireExplicitFormat()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Date;Description;Amount\n01/02/2026;Shop;-20.00\n03/02/2026;Bad;abc\n");
        using var input = new MemoryStream(bytes);
        Assert.All(new StatementParser().Csv(input, new()).Rows, r => Assert.NotNull(r.Error));
        using var explicitInput = new MemoryStream(bytes);
        var rows = new StatementParser().Csv(explicitInput, new(DateFormat: "dd/MM/yyyy")).Rows;
        Assert.Equal(new DateTime(2026, 2, 1), rows[0].Date); Assert.Null(rows[0].Error);
        Assert.NotNull(rows[1].Error); Assert.False(rows[1].Included);
    }
    [Fact]
    public void MappingReturnsHeadersWithoutGuessing()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("When,Merchant,Value\n2026-09-01,Shop,-50\n"));
        var parsed = new StatementParser().Csv(input, new());
        Assert.Empty(parsed.Rows); Assert.Equal(["When", "Merchant", "Value"], parsed.Columns);
    }
    [Fact]
    public void SyntheticPositionalPdfHandlesPagesContinuationSignsAndTotals()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < 2; i++)
        {
            var page = builder.AddPage(595, 842);
            void Text(string text, double x, double y) => page.AddText(text, 10, new PdfPoint(x, y), font);
            Text("ABSA", 30, 810);
            Text("Statement period 01/09/2026 to 30/09/2026", 30, 780);
            Text("Date", 30, 740); Text("Description", 110, 740); Text("Debit", 340, 740); Text("Credit", 420, 740); Text("Balance", 500, 740);
            Text(i == 0 ? "01/09/2026" : "02/09/2026", 30, 700);
            Text(i == 0 ? "Checkers" : "Salary", 110, 700);
            Text(i == 0 ? "1200.00" : "25000.00", i == 0 ? 340 : 420, 700);
            if (i == 0) Text("Cape Town", 110, 685);
            if (i == 1) { Text("Total debits 1200.00", 30, 600); Text("Total credits 25000.00", 30, 580); }
        }
        using var stream = new MemoryStream(builder.Build());
        var parsed = new StatementParser().Pdf(stream);
        Assert.Equal(2, parsed.Rows.Count); Assert.All(parsed.Rows, r => Assert.Null(r.Error));
        Assert.Equal("Checkers Cape Town", parsed.Rows[0].Description); Assert.Equal(-1200m, parsed.Rows[0].Amount);
        Assert.Equal(25000m, parsed.Rows[1].Amount); Assert.DoesNotContain(parsed.Notices, n => n.Contains("mismatch"));
    }
    [Fact]
    public void EmptyPdfIsClearlyRejected()
    {
        var builder = new PdfDocumentBuilder(); builder.AddPage(595, 842);
        using var stream = new MemoryStream(builder.Build());
        Assert.Contains("Scanned", Assert.Throws<ArgumentException>(() => new StatementParser().Pdf(stream)).Message);
    }
}
