namespace WarehouseGate.Infrastructure.Storage;

public interface IPhotoStorageService
{
    Task<string> SaveAsync(string transactionKey, string fileName, Stream content, CancellationToken ct = default);
}
