using System.DirectoryServices.Protocols;

using PingTrace.Core;

namespace PingTrace.Adapter.ActiveDirectory;

/// <summary>
/// Reads Active Directory domain diagnostics through a configured LDAP connection.
/// </summary>
/// <remarks>
/// The application owns connection configuration and disposal. Use this reader
/// with exclusive access to that connection: binding and reading must not
/// overlap with other operations on the same connection.
///
/// This first implementation uses synchronous LDAP operations. Cancellation
/// is checked between operations but cannot interrupt an executing Bind or
/// SendRequest call. Configure a finite connection timeout.
///
/// Reading the domain object after RootDSE exercises domain data access;
/// RootDSE metadata alone is not sufficient evidence of that access.
/// </remarks>
public sealed class LdapActiveDirectoryDiagnosticsReader : IActiveDirectoryDiagnosticsReader
{
    private readonly LdapConnection _connection;

    /// <summary>
    /// Initializes the reader with an application-owned connection.
    /// </summary>
    /// <param name="connection">
    /// The connection configured with the intended server and credentials.
    /// </param>
    public LdapActiveDirectoryDiagnosticsReader(LdapConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Binding uses the connection's configured credentials. Anonymous
    /// authentication is rejected because this probe is intended to exercise
    /// an authenticated directory connection.
    ///
    /// The returned task does not make the underlying LDAP calls asynchronous.
    /// </remarks>
    public Task<ActiveDirectoryDiagnostics> ReadDomainAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_connection.AuthType == AuthType.Anonymous)
        {
            throw new DirectoryDiagnosticException(
                TraceFailureKind.Configuration,
                "Ldap.AnonymousAuthentication",
                "The directory diagnostic reader requires authenticated LDAP access.");
        }

        _connection.Bind();

        cancellationToken.ThrowIfCancellationRequested();

        SearchResultEntry root = ReadEntry(
            distinguishedName: string.Empty,
            filter: "(objectClass=*)",
            attributes: ["dnsHostName", "defaultNamingContext"]);

        cancellationToken.ThrowIfCancellationRequested();

        string controller = ReadRequiredString(root, "dnsHostName");
        string domainDn = ReadRequiredString(root, "defaultNamingContext");

        SearchResultEntry domain = ReadEntry(
            distinguishedName: domainDn,
            filter: "(objectClass=domainDNS)",
            attributes: ["name"]);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ActiveDirectoryDiagnostics
        {
            DomainController = controller,
            DomainDistinguishedName = domainDn,
            DomainObjectName = ReadRequiredString(domain, "name")
        });
    }

    private SearchResultEntry ReadEntry(string distinguishedName, string filter, string[] attributes)
    {
        SearchRequest request = new(
            distinguishedName: distinguishedName,
            ldapFilter: filter,
            searchScope: SearchScope.Base,
            attributeList: attributes);

        SearchResponse response = (SearchResponse)_connection.SendRequest(request);

        return response.Entries.Count != 1
            ? throw new DirectoryDiagnosticException(
                TraceFailureKind.InvalidResponse,
                "Ldap.UnexpectedEntryCount",
                "The directory diagnostic query did not return exactly one entry.")
            : response.Entries[0];
    }

    private static string ReadRequiredString(SearchResultEntry entry, string attributeName)
    {
        DirectoryAttribute? attribute = entry.Attributes[attributeName];

        if (attribute is null || attribute.Count != 1)
        {
            throw new DirectoryDiagnosticException(
                TraceFailureKind.InvalidResponse,
                "Ldap.InvalidAttributeCount",
                $"The directory response must contain one '{attributeName}' value.");
        }

        object[] values = attribute.GetValues(typeof(string));

        return values[0] is not string value || string.IsNullOrWhiteSpace(value)
            ? throw new DirectoryDiagnosticException(
                TraceFailureKind.InvalidResponse,
                "Ldap.InvalidAttributeValue",
                $"The directory response contains an invalid '{attributeName}' value.")
            : value;
    }
}