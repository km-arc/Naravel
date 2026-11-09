# Review Report — Naravel.Filesystem

## Review metadata

- Package ID: Naravel.Filesystem.
- Source project: src/Naravel.Filesystem/Naravel.Filesystem.csproj.
- Baseline: main at 282a507bead92ae9ca40a9cb060baa2ee1dd7287.
- Review date: 2026-11-09.
- Reviewer mode: principal-architect source, security, test and documentation review; independent fresh-session review NOT RUN.
- Status: REVIEWED WITH FINDINGS (source inspection only; real S3 verification pending).
- Files inspected: IStorageDriver, StorageManager, options and registration, LocalStorageDriver, S3StorageDriver, StorageFake, local security/link tests, provider tests, package README and filesystem guide.
- Dependencies: optional AWS SDK S3 client for the S3 driver; the core filesystem abstraction and local driver do not require an S3 dependency.
- Verification performed: path normalization/root containment, symbolic-link rejection, stream/byte operations, copy/move/delete behavior, URL generation, driver registration and ownership inspected.
- Verification NOT RUN: restore, build, local filesystem tests, real S3-compatible service tests, symlink-swap race, mutation testing and independent review.

## Executive assessment

The package has a clear storage abstraction and keeps S3 optional. Local path handling checks that paths remain under the configured root and rejects symbolic links that point outside it. The tests cover traversal, file and directory links, and link operations.

One source-level defect affects URLs: the local driver and the non-expiring S3 URL path append the raw object path to a base URL without URL-encoding reserved characters. A valid storage key containing a question mark, hash sign or space can therefore produce a URL that the browser interprets as a different path, query or fragment.

## Package and dependency map

- Public contract: IStorageDriver.
- Manager/registration: StorageManager, FilesystemOptions, DiskOptions and FilesystemServiceCollectionExtensions.
- Drivers: LocalStorageDriver and optional S3StorageDriver.
- Test fake: StorageFake, an ordinary instance rather than global state.
- Project boundary: Filesystem core does not depend on S3; S3 client dependency is confined to its implementation.

## Findings

### [P2] FS-001 — Public file URLs do not encode reserved characters in the storage key

- Remediation state: OPEN.
- Evidence status: SEEN IN SOURCE; reserved-character URL test NOT RUN.
- Evidence: GetUrlAsync in src/Naravel.Filesystem/Drivers/LocalStorageDriver.cs builds a URL by joining _baseUrl with a relative path after replacing the directory separator only. The non-expiring branch of GetUrlAsync in src/Naravel.Filesystem/Drivers/S3StorageDriver.cs similarly joins service URL, bucket and raw key. StorageFake follows the same raw concatenation. Existing S3 URL tests use only folder/file.txt; local tests verify path/link security but do not test reserved URL characters.
- In everyday words: The storage driver can save a file whose name contains URL-special characters, but the link it returns does not protect those characters from being interpreted as URL syntax.
- Realistic example: A stored key named reports/a#final?.csv is appended directly to the URL. A browser treats #final?.csv as a fragment rather than part of the object key, so the link does not address the saved object.
- Why the evidence proves the risk: neither local nor non-expiring S3 URL construction encodes the key into a URL path component. Characters such as ? and # change how a URL is parsed.
- What is not proven: the source behavior is clear; the audit did not issue requests against a real local web server or S3-compatible service using reserved-character keys.
- Required safe outcome: every accepted storage key must be represented as a URL path without changing its identity. Encode each path segment while preserving slash separators, and keep S3 presigned URLs on the AWS SDK's signing path.
- Regression tests required: local and S3 non-expiring URL generation for spaces, ?, #, %, Unicode and nested path segments; parse the URL and prove the encoded path still names the original object key. Check the fake returns the same externally usable URL shape.
- Public API / PDR / release impact: URL behavior correction; no API change expected. Existing URLs containing reserved characters may change to their correctly escaped form.

## Existing guarantees and coverage

- Local path resolution rejects paths outside the configured root.
- Tests cover symbolic links to outside files and directories for read, write, delete, copy, move and URL operations.
- StorageFake follows the local driver's path rules and copies returned file bytes.
- S3 copy and move use the expected object operations; move is a copy followed by delete and is not an atomic object-store transaction.
- Expiring S3 URLs are created by the AWS SDK using a GetPreSignedUrlRequest; the finding concerns the non-expiring URL concatenation path.
- The local driver performs synchronous filesystem work inside Task-returning methods. This review did not establish a measured throughput defect, so it is recorded as an observation for future load assessment rather than a finding.

## Test assessment

- Tests inspected: LocalStorageDriverContractTests, LocalStorageDriverSecurityTests, LocalStorageDriverLinkOperationTests, StorageDriverContractTests, StorageManagerIntegrationTests and S3StorageDriverTests.
- Missing test: reserved-character storage keys must round-trip through generated URLs.
- Tests actually run in this review: NONE.
- Real S3, build, mutation testing and independent review: NOT RUN.

## Documentation and packaging assessment

- The package README and guide explain local root configuration, S3 options, public URLs and temporary signed URLs.
- Security notes describe the local filesystem root as an application-owned directory; the link tests prove that existing external symbolic links are rejected.
- No package sample or S3 service was executed in this audit.

## Risk summary and next action

- P2: FS-001.
- Recommended next task: add URL-path escaping and reserved-character tests; then verify local and S3-compatible behavior. No runtime change is authorized by this audit.

## Plain-language explanation

The filesystem package has strong path-boundary tests for the local driver, including links that point outside the storage root. The remaining issue is how returned links are built: the code joins the raw file name to a URL instead of encoding it. That can make a valid file name turn into a different browser URL. The source was inspected, but the reserved-character behavior was not tested against a running server.
