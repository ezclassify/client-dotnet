# EzClassify.Rest.Contract

Generated C# wire models and response parsers for the EzClassify REST API.

Types are generated from the EzClassify REST contract: its OpenAPI document and JSON Schema definitions.

## Install

```
dotnet add package EzClassify.Rest.Contract
```

## Contents

- [`Models.g.cs`](Models.g.cs): generated record types mirroring the EzClassify wire format
- [`ContextModels.g.cs`](ContextModels.g.cs): record types for the context-bound operations
- [`ContextEnvelopeModels.g.cs`](ContextEnvelopeModels.g.cs): the resolution envelope every context operation returns
- [`DocumentAssessmentModels.g.cs`](DocumentAssessmentModels.g.cs): record types for document audit and access assessment
- [`WireEnums.g.cs`](WireEnums.g.cs): the wire enums, extensible structs that keep the raw token so a value a newer server adds still round-trips
- [`ResultParser.cs`](ResultParser.cs): helpers for deserializing REST responses
- [`ContextResultParser.cs`](ContextResultParser.cs): helpers for deserializing context-operation responses

## License

Apache-2.0
