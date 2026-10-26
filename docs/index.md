# EzClassify .NET SDK reference

This reference covers the public types and members in `EzClassify.Rest.Contract`
and `EzClassify.Rest.Client`, including generated wire models. It is extracted
from the common `netstandard2.0` API and XML comments. The client also targets
`net8.0`; both frameworks share this public API.

Run `make docs` from the repository root with the SDK selected by [`global.json`](../global.json).
It builds the solution, restores the pinned DocFX 2.78.5 tool, and writes HTML
to `artifacts/docs/index.html`. The initial restore requires NuGet access.
Both shipped libraries also emit XML documentation beside their DLLs for NuGet
consumers and IntelliSense. Generated metadata in `docs/api` and HTML in
`artifacts/docs` are ignored build outputs.

The generated reference excludes internal transport and parser helpers, tests,
the server-tour application, build scripts and compiler compatibility shims.
See the repository README for credentials, Context binding, response caching,
recorded integration scenarios and live-server prerequisites.

The SDK forwards operations to a licensed REST deployment. Its generated model
descriptions explain the wire contract; they do not implement engine policy.
