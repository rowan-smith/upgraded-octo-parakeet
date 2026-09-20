namespace Platform.Contracts.Audit;

public interface IAuditWriter
{
    void Write(string module, string action, string resource, object? metadata = null);
}
