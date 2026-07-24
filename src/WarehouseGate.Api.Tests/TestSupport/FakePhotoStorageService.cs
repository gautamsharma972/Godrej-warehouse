using WarehouseGate.Infrastructure.Storage;

namespace WarehouseGate.Api.Tests.TestSupport;

// None of the paths under test (assign-supervisor guard, complete-with-exceptions) upload a
// photo - test data seeds PhotoEvidence/OutwardPhotoEvidence rows directly - so this only needs
// to exist to satisfy InwardService/OutwardService's constructor.
public sealed class FakePhotoStorageService : IPhotoStorageService
{
    public Task<string> SaveAsync(string transactionKey, string fileName, Stream content, CancellationToken ct = default) =>
        throw new NotSupportedException("Photo upload is not exercised by these tests.");
}
