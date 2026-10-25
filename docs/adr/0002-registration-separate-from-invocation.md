# Registration is separate from invocation

The catalog holds what exists: names, types and descriptions of actions and context providers. Factories (`IActionFactory`, `IContextProviderFactory`) create instances. The engine looks a name up in the catalog and gives the factory the registration, so a factory never resolves by name and the catalog is the only source of truth for which names exist. Factories are kept, rather than having the engine take an `IServiceProvider`, so that any container (or none) can plug in through one narrow contract per kind.

## Considered Options

- **The engine takes `IServiceProvider` and the factories are removed.** It needed fewer types, but it mixed what is registered with how instances are created.
- **Factories resolve by name (the 0.5 contract).** Rejected: it gives two sources of truth for which actions exist.
