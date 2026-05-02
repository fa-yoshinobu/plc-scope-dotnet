namespace PlcScope.Infrastructure.Protocols;

using PlcScope.Core.Abstractions;
using PlcScope.Core.Models;

public sealed class PlcSessionFactory : IPlcSessionFactory
{
    public Task<IPlcSession> CreateAsync(ConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        IPlcSession session = settings.Protocol switch
        {
            ProtocolKind.Slmp => new SlmpSession(settings),
            ProtocolKind.HostLink => new HostLinkSession(settings),
            ProtocolKind.Toyopuc => new ToyopucSession(settings),
            _ => throw new ArgumentOutOfRangeException(nameof(settings.Protocol), settings.Protocol, null),
        };

        return Task.FromResult(session);
    }
}
