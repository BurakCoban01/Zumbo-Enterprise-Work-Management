# API Compatibility

Business APIs are controller-based and preserve a consistent response envelope. Compatibility review covers more than route names.

Check:

- route and HTTP method;
- authentication and permission policy;
- request binding and validation;
- response status and body schema;
- correlation and error envelope;
- ETag/concurrency headers;
- file content/disposition behavior;
- webhook signature and retry semantics;
- rate limits and transaction boundaries;
- OpenAPI operation/schema output.

`contracts/openapi.v1.json` is the versioned baseline. API tests cover runtime semantics that OpenAPI cannot express. A passing schema comparison alone does not prove authorization, header or side-effect compatibility.

When a breaking change is unavoidable, provide an explicit compatibility/versioning plan before replacing the baseline.
