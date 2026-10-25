namespace RuleLoom.Schemas;

/// <summary>
/// Where a catalog is kept between runs (in memory, a database, ...). A catalog is built from code plus what its store
/// holds: the store can add event and context schemas that no code defines, but code always wins over stored entries of
/// the same name. The store is read when the catalog is built, and the built catalog is saved back.
/// </summary>
public interface ICatalogStore
{
    /// <summary>
    /// Loads everything stored; an empty document when nothing is.
    /// </summary>
    ValueTask<CatalogDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves <paramref name="document"/>'s entries: each event, context, operator and action replaces the stored entry
    /// of the same kind and name (ignoring case) or is added. Nothing is deleted, and problems aren't stored (they're
    /// found again by every build).
    /// </summary>
    ValueTask SaveAsync(CatalogDocument document, CancellationToken cancellationToken = default);
}
