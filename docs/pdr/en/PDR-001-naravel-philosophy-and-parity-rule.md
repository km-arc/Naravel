# PDR-001: Naravel philosophy and the parity rule

- **Status:** Accepted
- **Laravel equivalent:** the whole framework
- **Native .NET equivalent:** the BCL and `Microsoft.Extensions.*` (DI, Options, Configuration, Logging, Hosting)

## Context
The goal is to rebuild Laravel's capabilities in .NET. A naive port copies PHP mechanics (magic methods, untyped arrays,
reflection by naming convention) that .NET does not need and that make the result feel foreign and worse.

## Decision
1. Implement a Laravel feature **only if it brings value** on top of what .NET already offers.
2. If .NET solves it natively and idiomatically, use the native solution and document it.
3. Keep Laravel's *concepts, vocabulary and structure* so Laravel developers feel at home; use .NET's *strengths* (types, async, DI, Options) to implement them.
4. Every adopt/adapt/reject decision is recorded as a PDR **before** code, with rejected alternatives.
5. Everything ships with tests and English + Persian documentation.

## Consequences
- Some Laravel features will intentionally not exist (see `laravel-parity.md`).
- More writing upfront; much less rework and much clearer intent for future contributors and AI agents.

## Rejected alternatives
- *Line-by-line port:* produces non-idiomatic APIs and needless code.
- *Use only .NET natives, no Naravel:* loses the Laravel vocabulary and structure the project exists to provide.
