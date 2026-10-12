namespace SchemaLence.Core.Connections;

public sealed record DatabaseConnectionProfile(
    string Id,
    string Name,
    string ProviderId,
    string Host,
    int Port,
    string Database,
    string Username,
    string SslMode);

public sealed record DatabaseConnectionInput(
    string Name,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    string SslMode = "Require",
    string? Options = null);

public sealed record ConnectionTestResult(
    bool Succeeded,
    string ServerDescription,
    string DatabaseName,
    string Message);
