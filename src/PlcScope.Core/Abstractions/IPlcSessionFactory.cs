namespace PlcScope.Core.Abstractions;

using PlcScope.Core.Models;

public interface IPlcSessionFactory
{
    Task<IPlcSession> CreateAsync(ConnectionSettings settings, CancellationToken cancellationToken = default);
}
