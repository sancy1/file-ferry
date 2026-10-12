namespace SchemaLence.Core.Providers;

public sealed class DatabaseProviderException : Exception
{
    public DatabaseProviderException(
        string operation,
        string diagnosticCode,
        Exception innerException)
        : base($"{operation} failed ({diagnosticCode}).", innerException)
    {
        Operation = operation;
        DiagnosticCode = diagnosticCode;
    }

    public string Operation { get; }
    public string DiagnosticCode { get; }
}
