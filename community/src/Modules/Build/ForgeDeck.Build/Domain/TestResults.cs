namespace ForgeDeck.Build.Domain;

public sealed class TestRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Test Results";
    public List<TestSuite> Suites { get; init; } = [];
    public int Total => Suites.Sum(s => s.Total);
    public int Passed => Suites.Sum(s => s.Passed);
    public int Failed => Suites.Sum(s => s.Failed);
    public int Skipped => Suites.Sum(s => s.Skipped);
    public TimeSpan Duration => TimeSpan.FromSeconds(Suites.Sum(s => s.Duration.TotalSeconds));
}

public sealed class TestSuite
{
    public string Name { get; set; } = string.Empty;
    public List<TestCase> Cases { get; init; } = [];
    public int Total => Cases.Count;
    public int Passed => Cases.Count(c => c.Outcome == TestOutcome.Passed);
    public int Failed => Cases.Count(c => c.Outcome == TestOutcome.Failed);
    public int Skipped => Cases.Count(c => c.Outcome == TestOutcome.Skipped);
    public TimeSpan Duration => TimeSpan.FromSeconds(Cases.Sum(c => c.Duration.TotalSeconds));
}

public sealed class TestCase
{
    public string Name { get; set; } = string.Empty;
    public string? ClassName { get; set; }
    public TestOutcome Outcome { get; set; }
    public TimeSpan Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}
