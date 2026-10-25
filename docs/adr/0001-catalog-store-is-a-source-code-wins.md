# The catalog store is a source, code always wins, and it is read only at startup

A catalog store is treated as an ordinary store, whatever the deployment shape. That means it can add event and context schemas that no code defines, not just mirror the code. When code and store disagree about an entry, code wins and its version is written back: a stored schema that contradicts the code is developer error, and the code defines what the application actually raises. Operators and actions only ever come from code, because without code they can't run. The store is read once, when the catalog is built. The catalog stays immutable, so a stored change takes effect on restart.

## Considered Options

- **The store as a mirror only.** It was simpler, but it would have tied the store's role to a monolith. The service-to-service setup needs the store as a source.
- **The store wins over code.** Rejected: a UI edit could then change the shape of an event that the code raises differently.
- **A rebuilding "current catalog" holder.** Deferred, not rejected. Service-to-service will need it, and it can wrap a startup-built catalog later without changing this design.

## Consequences

- Building with a store is asynchronous (`BuildAsync`). A plain `Build()` never touches I/O.
- Saving upserts entries by kind and name and never deletes them. Concurrent writers (scaled-out instances, publishing producers) therefore only conflict on the same entry, and the last write wins. Each process still runs on its own in-memory catalog.
- Broken links in code-defined entries fail the build, because they are this application's mistake. Broken links in store-contributed entries become catalog problems instead, which degrade only the rules that touch them, so an outside publish can never stop the application from starting.
- In DI, a catalog is resolved per scope from an internal holder that is set once at startup. Reloading later only swaps the holder's catalog.
