# Native toolchain image

[`dotnet-toolchain.Dockerfile`](dotnet-toolchain.Dockerfile) combines the official, digest-pinned .NET 10 SDK
and Node 24 images. Make and Python support the existing Makefile and notices
gate. Image construction installs the toolchain only; it does not generate SDK
models, restore SDK project dependencies, or run SDK builds and tests.

Build it from an otherwise empty context containing this Dockerfile:

```sh
docker build --pull=false --file dotnet-toolchain.Dockerfile --tag ezclassify/client-dotnet-toolchain:local .
```

Run the repository's normal `make regen`, `make build`, `make test` and
boundary gates inside the verified image. Mount only this checkout and private
caches. Supply a private home, `DOTNET_CLI_HOME` and `NUGET_PACKAGES`; do not
mount host NuGet configuration or a host home directory. Image tooling is not
part of any published NuGet package. The repository's dependency notices and
SBOM gates still apply to the actual SDK closure.
