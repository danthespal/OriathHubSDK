# OriathHub plugin documentation

Build OriathHub plugins against the `OriathHub.Sdk` package. A plugin author needs the SDK package, the docs, and a host install to test against; they do not need the OriathHub source tree.

## Guides

| Guide | What it covers |
|---|---|
| [Getting started](getting-started.md) | Create a plugin project, write your first class, build and install. |
| [Plugin lifecycle](plugin-lifecycle.md) | When each `PluginBase` method runs, how to write settings, and how to use coroutines. |
| [Plugin examples](examples.md) | Small, focused examples for common plugin tasks. |
| [API reference](api-overview.md) | Properties and methods exposed by the host: `Core`, entities, components, UI panels, events, drawing, input, and raw memory reads. |
| [Gotchas](gotchas.md) | Shared assembly rules, deployment requirements, resource cleanup, and reload behavior. |

A complete, commented example lives in [`samples/SampleHelloWorld/`](../samples/SampleHelloWorld).
