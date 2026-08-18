# API Contracts

`openapi.v1.json` is the versioned compatibility baseline for Zumbo's business HTTP API.

The baseline is consumed by API/OpenAPI tests to detect accidental changes to routes, methods and schemas. It is not generated documentation to refresh automatically after any diff.

## Updating the baseline

1. Generate the current OpenAPI document with the repository contract test.
2. Review route, method, authorization, status, header and schema changes.
3. Update clients or provide compatibility behavior where required.
4. Replace the baseline only when the public-contract change is intentional.
5. Run API, gateway and frontend contract tests.

Do not include environment URLs, credentials or runtime data in a committed contract.
