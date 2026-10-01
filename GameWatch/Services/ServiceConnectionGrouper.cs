using System.IO;

namespace GameWatch.Services;

public record ServiceEndpointGroup(string Label, IReadOnlyList<string> Endpoints);

public static class ServiceConnectionGrouper
{
    public static IReadOnlyList<ServiceEndpointGroup> Group(IEnumerable<ModuleConnection> connections, IEnumerable<HostedService> hostedServices)
    {
        var services = hostedServices.ToArray();
        return connections.GroupBy(item => Label(item.ModuleName, services), StringComparer.OrdinalIgnoreCase)
            .Select(group => new ServiceEndpointGroup(group.Key, group.Select(item => Endpoint(item.Connection))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(endpoint => endpoint).ToArray()))
            .OrderBy(group => group.Label).ToArray();
    }

    private static string Label(string module, IReadOnlyList<HostedService> services)
    {
        if (string.IsNullOrWhiteSpace(module)) return "Unidentified owner";
        var file = Path.GetFileName(module);
        var matches = services.Where(service =>
            string.Equals(service.Name, module, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(service.DisplayName, module, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(service.ModulePath) &&
             string.Equals(Path.GetFileName(service.ModulePath), file, StringComparison.OrdinalIgnoreCase)))
            .Select(service => service.DisplayName).Distinct().ToArray();
        return matches.Length > 0 ? string.Join(" / ", matches) : $"Module: {module}";
    }

    private static string Endpoint(ConnectionRecord connection) => connection.Protocol == TransportProtocol.Udp
        ? $"UDP bound {connection.LocalAddress}:{connection.LocalPort} (remote unavailable)"
        : connection.RemotePort > 0
            ? $"TCP {connection.RemoteAddress}:{connection.RemotePort}"
            : $"TCP listening {connection.LocalAddress}:{connection.LocalPort}";
}
