using System.Globalization;
using System.Xml.Linq;
using ForgeDeck.Build.Domain;

namespace ForgeDeck.Build.Application;

public static class TrxParser
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static TestRun Parse(string trxXml)
    {
        var document = XDocument.Parse(trxXml);
        var root = document.Root ?? throw new InvalidOperationException("TRX document has no root.");
        var run = new TestRun
        {
            Name = root.Attribute("name")?.Value ?? "Test Results"
        };

        var results = root.Descendants(Ns + "UnitTestResult")
            .Concat(root.Descendants("UnitTestResult"))
            .ToArray();

        var suite = new TestSuite { Name = run.Name };
        foreach (var result in results)
        {
            var outcomeText = result.Attribute("outcome")?.Value ?? "Failed";
            var outcome = outcomeText.ToLowerInvariant() switch
            {
                "passed" => TestOutcome.Passed,
                "failed" => TestOutcome.Failed,
                "notexecuted" or "skipped" => TestOutcome.Skipped,
                _ => TestOutcome.Failed
            };

            var duration = TimeSpan.Zero;
            if (TimeSpan.TryParse(result.Attribute("duration")?.Value, CultureInfo.InvariantCulture, out var parsed))
            {
                duration = parsed;
            }

            var error = result.Descendants(Ns + "Message").Concat(result.Descendants("Message")).FirstOrDefault()?.Value
                        ?? result.Descendants(Ns + "ErrorInfo").Concat(result.Descendants("ErrorInfo")).FirstOrDefault()?.Value;
            var stack = result.Descendants(Ns + "StackTrace").Concat(result.Descendants("StackTrace")).FirstOrDefault()?.Value;

            suite.Cases.Add(new TestCase
            {
                Name = result.Attribute("testName")?.Value ?? "Unknown",
                ClassName = result.Attribute("testId")?.Value,
                Outcome = outcome,
                Duration = duration,
                ErrorMessage = error,
                StackTrace = stack
            });
        }

        run.Suites.Add(suite);
        return run;
    }
}
