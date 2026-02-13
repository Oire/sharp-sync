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

### Sample Code Review (2026-02-13)
Reviewed all files in `samples/SharpSync.Samples.Console/`:
- Fixed brace placement for all class declarations, method declarations, control flow statements, and exception handlers
- Fixed object initializer bracing (dictionaries, object initializers with `new`)
- Files affected: `Program.cs`, `BasicSyncExample.cs`, `ConsoleOAuth2Example.cs`
- All files now comply with `.editorconfig` brace placement rules
- Build verification: ✅ No compilation errors

## Style Enforcement Learnings

### Opening Brace Placement
Per `.editorconfig`: `csharp_new_line_before_open_brace = none` means opening braces go on the SAME line as the preceding code element (class name, method name, if/for/while, catch/finally, etc.). This project uses Allman/BSD style consistently.

**Correct pattern:**
```csharp
public class MyClass
{
    public void MyMethod()
    {
        if (condition)
        {
            // code
        }
    }
}
```

### Common Patterns Requiring Fixes in Sample Code
1. **Lambda expressions with blocks**: Opening brace on new line after `=>`
2. **Dictionary initializers**: Opening brace on new line after `new Dictionary<TKey, TValue>`
3. **Exception handlers**: `catch` and `finally` clauses need opening braces on new line
4. **Else clauses**: Despite setting `csharp_new_line_before_else = false`, opening brace for else block goes on new line
