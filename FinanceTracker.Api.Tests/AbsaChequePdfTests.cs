using FinanceTracker.Api.Services;
using Xunit;

namespace FinanceTracker.Api.Tests;

public class AbsaChequePdfTests
{
    [Fact]
    public void ChequeLayoutReadsPeriodPagesWrappedDescriptionsFeesAndRunningBalances()
    {
        using var source = new MemoryStream(ChequePdfFixture.Example());
        var parsed = new StatementParser().Pdf(source);
        Assert.Equal(new DateTime(2026, 10, 17), parsed.Start);
        Assert.Equal(new DateTime(2026, 11, 16), parsed.End);
        Assert.Equal(7, parsed.Rows.Count);
        var valid = parsed.Rows.Where(r => r.Error == null).ToList();
        Assert.Equal(6, valid.Count);
        Assert.All(valid, row => Assert.True(row.Included));
        Assert.Equal(2199.99m, valid.Where(r => r.Amount > 0).Sum(r => r.Amount));
        Assert.Equal(-217.09m, valid.Where(r => r.Amount < 0).Sum(r => r.Amount));
        Assert.Equal("Digital Payment Cr Settlement Absa Bank Demo payer", valid[0].Description);
        Assert.Equal("Overseas Purchase Settlement Card No. 0000 Demo Store Cape Town (Effective 28/10/2026)", valid[1].Description);
        Assert.Equal(new DateTime(2026, 10, 30), valid[1].Date); // Posting date, not effective date.
        Assert.Equal(-1.86m, valid[2].Amount);
        Assert.Equal("Transaction Fee Headoffice Overseas Purchase (281026) 74.50-", valid[2].Description);
        Assert.Equal(new DateTime(2026, 11, 1), valid[3].Date);
        Assert.Equal(-10.80m, valid[4].Amount); // Do not import the 0.00 fee reference.
        var information = Assert.Single(parsed.Rows, r => r.Error != null);
        Assert.Equal(0m, information.Amount); Assert.False(information.Included);
        Assert.Contains("Balance-only", information.Error);
        Assert.Contains("18 Sms Notifications", information.Description);
        Assert.Contains(parsed.Notices, n => n.StartsWith("Running balances reconcile"));
        Assert.DoesNotContain(parsed.Notices, n => n.Contains("mismatch"));
        Assert.DoesNotContain(parsed.Rows, r => r.Description.Contains("PRICING") || r.Description.Contains("Privacy") || r.Description.Contains("VAT"));
    }

    [Theory]
    [InlineData("1 23.45")][InlineData("1,2")][InlineData("-25.00")][InlineData("12.345")][InlineData("unknown")]
    public void MalformedOrNegativeDebitAmountsRemainExcluded(string amount)
    {
        var parsed = Parse(new("17/10/2026", "Bal Brought Forward", Balance: "250.00"),
            new("1/11/2026", "Demo purchase", Debit: amount, Balance: "225.00"));
        var row = Assert.Single(parsed.Rows);
        Assert.NotNull(row.Error); Assert.False(row.Included); Assert.Null(row.Amount);
    }

    [Fact]
    public void BalanceMismatchIsExcludedAndLaterRowsUseTheNextReadableBalance()
    {
        var parsed = Parse(new("17/10/2026", "Bal Brought Forward", Balance: "250.00"),
            new("1/11/2026", "Demo purchase", Debit: "25.00", Balance: "224.00"),
            new("2/11/2026", "Next purchase", Debit: "10.00", Balance: "214.00"));
        Assert.Contains("Running balance does not match", parsed.Rows[0].Error);
        Assert.False(parsed.Rows[0].Included); Assert.Null(parsed.Rows[1].Error);
        Assert.Contains(parsed.Notices, n => n.Contains("mismatch"));
    }

    [Fact]
    public void MissingAmountWithChangedBalanceIsNotTreatedAsZeroMovement()
    {
        var parsed = Parse(new("17/10/2026", "Bal Brought Forward", Balance: "250.00"),
            new("1/11/2026", "Unreadable purchase", Balance: "225.00"));
        var row = Assert.Single(parsed.Rows);
        Assert.Null(row.Amount); Assert.False(row.Included);
        Assert.Contains(parsed.Notices, n => n.Contains("mismatch"));
    }

    [Fact]
    public void BothDebitAndCreditAndUnreadableBalanceAreRejected()
    {
        var parsed = Parse(new("17/10/2026", "Bal Brought Forward", Balance: "250.00"),
            new("1/11/2026", "Ambiguous purchase", Debit: "25.00", Credit: "10.00", Balance: "235.00"),
            new("2/11/2026", "Unreadable balance", Debit: "10.00", Balance: "unknown"));
        Assert.All(parsed.Rows, r => { Assert.NotNull(r.Error); Assert.False(r.Included); });
        Assert.Contains("single positive debit or credit", parsed.Rows[0].Error);
        Assert.Contains("Unreadable running balance", parsed.Rows[1].Error);
    }

    [Fact]
    public void SpaceGroupedLargeAmountsAndTrailingNegativeBalancesArePreserved()
    {
        var parsed = Parse(new("17/10/2026", "Bal Brought Forward", Balance: "0.00"),
            new("1/11/2026", "Large purchase", Debit: "1 234 567.89", Balance: "1 234 567.89-"));
        var row = Assert.Single(parsed.Rows);
        Assert.Null(row.Error); Assert.Equal(-1234567.89m, row.Amount);
        Assert.Equal("Large purchase", row.Description);
        Assert.Contains(parsed.Notices, n => n.StartsWith("Running balances reconcile"));
    }

    [Theory]
    [InlineData("31/02/2026", "Unreadable transaction date")]
    [InlineData("1/12/2026", "outside the statement period")]
    public void InvalidAndOutOfPeriodDatesAreExcluded(string date, string error)
    {
        var parsed = Parse(new("17/10/2026", "Bal Brought Forward", Balance: "250.00"),
            new(date, "Demo purchase", Debit: "25.00", Balance: "225.00"));
        Assert.Contains(error, Assert.Single(parsed.Rows).Error); Assert.False(parsed.Rows[0].Included);
    }

    [Fact]
    public void ReversedPeriodAndUnsupportedBankAreRejected()
    {
        ChequeRow[][] rows = [[new("1/11/2026", "Demo purchase", Debit: "25.00", Balance: "225.00")]];
        using var reversed = new MemoryStream(ChequePdfFixture.Build(rows, period: "16 Nov 2026 to 17 Oct 2026"));
        Assert.Contains("period", Assert.Throws<ArgumentException>(() => new StatementParser().Pdf(reversed)).Message);
        using var other = new MemoryStream(ChequePdfFixture.Build(rows, bank: "Other bank"));
        Assert.Contains("Unsupported", Assert.Throws<ArgumentException>(() => new StatementParser().Pdf(other)).Message);
    }

    private static ParsedStatement Parse(params ChequeRow[] rows)
    {
        using var source = new MemoryStream(ChequePdfFixture.Build([rows]));
        return new StatementParser().Pdf(source);
    }
}
