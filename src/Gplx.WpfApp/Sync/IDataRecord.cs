using System;

namespace Gplx.WpfApp.Sync;

public interface IDataRecord
{
    string Id { get; }
    string Content { get; }
    DateTime LastModified { get; }
}
