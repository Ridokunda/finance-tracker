using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace FinanceTracker.Api.Tests;

// Invented content at the column positions observed in the supplied cheque PDF.
// Never store the user's statement or identifying details in test fixtures.
internal record ChequeRow(string Date, string Description, string Debit = "", string Credit = "",
    string Balance = "", string[]? Continuation = null, bool Charge = false);

internal static class ChequePdfFixture
{
    internal static byte[] Build(ChequeRow[][] pages, string period = "17 Oct 2026 to 16 Nov 2026", string bank = "ABSA")
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages.Length; i++)
        {
            var page = builder.AddPage(596, 842);
            void Text(string text, double x, double y) => page.AddText(text, 8.5, new PdfPoint(x, y), font);
            double Width(string text) => page.MeasureText(text, 8.5, new PdfPoint(0, 0), font).Max(l => l.GlyphRectangle.Right);
            void Right(string text, double right, double y) { if (text.Length > 0) Text(text, right - Width(text), y); }
            Text(bank, 30, 810);
            Text(i == 0 ? "Cheque account statement" : "Your transactions (continued)", 30, 780);
            if (i == 0) Text(period, 170, 760);
            Text("Date", 28.4, 720); Text("Transaction Description", 85, 720); Text("Charge", 264.4, 720);
            Text("Debit Amount", 344.4, 720); Text("Credit Amount", 426.5, 720); Text("Balance", 536.6, 720);
            var y = 700d;
            foreach (var row in pages[i])
            {
                Text(row.Date, 28.4, y); Text(row.Description, 85, y);
                if (row.Charge) Text("*", 304, y);
                Right(row.Debit, 344.4 + Width("Debit Amount"), y);
                Right(row.Credit, 426.5 + Width("Credit Amount"), y);
                Right(row.Balance, 536.6 + Width("Balance"), y);
                foreach (var continuation in row.Continuation ?? []) { y -= 12; Text(continuation, 99.2, y); }
                y -= 20;
            }
            Text(i == 0 ? "Our Privacy Notice has been updated." : "YOUR PRICING PLAN : DEMO ACCOUNT", 56, y - 12);
            Text("CREDIT INTEREST RATE AS AT 17/10/2026", 56, y - 25);
            Text("* = VAT R1.00- INCLUDED", 56, y - 38);
            Text($"Page {i + 1} of {pages.Length} {bank} Bank Limited Tax Invoice", 30, 40);
        }
        return builder.Build();
    }

    internal static byte[] Example() => Build([
        [new("17/10/2026", "Bal Brought Forward", Balance: "250.00"),
         new("22/10/2026", "Digital Payment Cr Settlement", Credit: "1 200.00", Balance: "1 450.00", Continuation: ["Absa Bank Demo payer"]),
         new("30/10/2026", "Overseas Purchase Settlement", Debit: "74.50", Balance: "1 375.50", Continuation: ["Card No. 0000 Demo Store Cape Town", "(Effective 28/10/2026)"]),
         new("30/10/2026", "Transaction Fee Headoffice", Debit: "1.86", Balance: "1 373.64", Charge: true, Continuation: ["Overseas Purchase (281026) 74.50-"])],
        [new("1/11/2026", "Pos Purchase Settlement", Debit: "129.93", Balance: "1 243.71", Continuation: ["Card No. 0000 Demo Taxi Cpt", "(Effective 29/10/2026)"]),
         new("6/11/2026", "Notific Fee Sms Notifyme", Balance: "1 243.71", Continuation: ["18 Sms Notifications"]),
         new("6/11/2026", "Transaction Fee Headoffice", Debit: "10.80", Balance: "1 232.91", Charge: true, Continuation: ["Notific Fee Sms (061126) 0.00"]),
         new("10/11/2026", "Acb Credit Settlement", Credit: "999.99", Balance: "2 232.90", Continuation: ["Demo withdrawal"])]]);
}
