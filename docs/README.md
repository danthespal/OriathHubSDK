# OriathHub plugin documentation

Build plugins for OriathHub against the `OriathHub.Sdk` package — compiled DLLs and docs only, no source tree required.

## Guides

| Guide | What it covers |
|---|---|
| [Getting started](getting-started.md) | Create a plugin project, write your first class, build and install. |
| [Plugin lifecycle](plugin-lifecycle.md) | When each `PluginBase` method runs, how to write settings, and how to use coroutines. |
| [API reference](api-overview.md) | Every property and method exposed by the host — `Core`, entities, all components, UI panels, events, drawing, input, raw memory reads. |
| [Gotchas](gotchas.md) | Shared assembly rules, deployment requirements, resource cleanup, and reload behaviour. |

A complete, commented example lives in [`samples/SampleHelloWorld/`](../samples/SampleHelloWorld).
