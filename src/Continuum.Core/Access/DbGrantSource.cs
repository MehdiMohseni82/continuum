using Continuum.Core.Data;
using Continuum.Core.Domain;

namespace Continuum.Core.Access;

/// <summary>
/// Reads grants straight from the database as a composable query, so a visibility predicate becomes
/// one EXISTS inside the caller's own statement rather than a second round trip per request.
/// </summary>
public sealed class DbGrantSource(ContinuumDbContext db) : IGrantSource
{
    public IQueryable<Grant> Grants => db.Grants;
}
