namespace MQTTower.Core.Interfaces;

public interface IBackupService
{
    Task<byte[]> CreateBackupArchiveAsync(CancellationToken cancellationToken = default);
    Task RestoreFromArchiveAsync(Stream zipStream, CancellationToken cancellationToken = default);
}
