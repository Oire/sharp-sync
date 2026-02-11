# .NET Style Corrector Memory

## Project Style Patterns

### Object Initializer Formatting
When combining constructor parameters with object initializer syntax, place initializer properties on separate lines:

```csharp
// Correct
new ChangeInfo(Path: path, ChangeType: type) {
    DetectedAt = timestamp
}

// Avoid (single line can be hard to read)
new ChangeInfo(Path: path, ChangeType: type) { DetectedAt = timestamp }
```

This pattern appears in storage implementations (S3Storage, WebDavStorage) when creating `ChangeInfo` records with `DetectedAt` property.

## Code Quality Notes

### ChangeInfo Record Refactoring
Recent refactor moved from tuple-based batch signatures to strongly-typed `ChangeInfo` record. `DetectedAt` changed from positional parameter to init-only property with `DateTime.UtcNow` default. All usages reviewed and compliant with style rules.

### Test Code Quality
Test code in this project follows consistent patterns:
- Clear Arrange-Act-Assert structure
- Descriptive test names with `MethodName_Scenario_ExpectedResult` format
- Good use of collection expressions (`Array.Empty<T>()`, `new[] { ... }`)
- Thread safety tests use proper error capturing with lock statements
