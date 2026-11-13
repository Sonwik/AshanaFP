namespace Ashana.Domain.Common;

/// <summary>
/// Something with an identity that is stored. Entities are immutable records:
/// every change produces a new record with <see cref="Version"/> increased by one.
/// </summary>
public interface IEntity
{
    Guid Id { get; }

    /// <summary>
    /// Optimistic concurrency token. Increased on every change, so two admins editing the same record
    /// cannot silently overwrite each other: an edit based on an old version is rejected.
    /// </summary>
    int Version { get; }
}
