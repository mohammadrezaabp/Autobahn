# Autobahn development conventions

- Keep Domain independent of frameworks, Application dependent only on Domain, and adapters in Infrastructure or their gateway module.
- Put behavior and invariants in the relevant domain model. Keep HTTP handlers focused on transport and authorization.
- Every public type belongs in its own C# file. Run the code layout check before finishing changes.
- Use nullable reference types, asynchronous I/O, cancellation tokens and dependency injection. Keep package versions centralized.
- Never commit local credentials, model data, databases, generated bundles or environment-specific host inventories.
- Keep changes self-contained and use generic sample configuration. Preserve module boundaries and architecture tests.
- Test domain rules, tenant isolation, streaming, state transitions and failure handling. Hardware integration requires a configured test host.
