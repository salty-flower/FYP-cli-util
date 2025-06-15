# DataCollection Project: Comprehensive Refactor Plan

## Executive Summary

The DataCollection project has grown from a single large csproj into 4 modules, but significant code quality issues remain, particularly in the Presentation.Cli layer. This plan outlines a systematic refactor to adopt functional programming patterns using CSharpFunctionalExtensions, improve separation of concerns, and eliminate major code smells.

## Current State Analysis

### Critical Code Smells Identified

#### 1. God Classes & Violation of Single Responsibility Principle

- **BugListDiscoveryCommands.cs**: 1,272 lines handling CLI parsing, business orchestration, UI rendering, validation, persistence, and error logging
- **ProcedureCommands.cs**: 608 lines mixing procedural logic with presentation concerns
- **ComprehensiveBugListDiscovery method**: 250+ lines with high cyclomatic complexity

#### 2. Exception Handling Anti-patterns

- 50+ `catch (Exception ex)` blocks swallowing all exception types
- Integer return codes (`return 0/1`) instead of proper error modeling
- No domain-specific error types or error composition

#### 3. Magic Numbers & Configuration Sprawl

- Constants scattered across 6+ static classes (`BugListConstants`, `BugListPatterns`, etc.)
- Hardcoded confidence thresholds, file patterns, regex patterns
- No central configuration or runtime flexibility

#### 4. Leaky Abstractions & Mixed Concerns

- CLI layer directly performing database operations
- Spectre.Console rendering mixed with business logic
- Python interop in presentation layer instead of application layer

#### 5. Primitive Obsession & Parameter Bloat

- Methods with 8-10+ parameters
- String-based DOI/URL handling without value objects
- No command/query parameter objects

#### 6. Duplicate Code Patterns

- Table building/rendering logic repeated across commands
- Error logging + console display patterns duplicated
- Safe markup and truncation helpers copy-pasted

#### 7. Async/Await Issues

- Many methods ignore provided `CancellationToken` parameters
- Synchronous I/O operations in async methods
- Poor async composition patterns

## Target Architecture

### Functional Programming Foundation

- Adopt **CSharpFunctionalExtensions** for functional programming constructs. See "./CSharpFunctionalExtensions_README.md" for more information.

### Clean Architecture Principles

- **Pure Application Layer**: No UI dependencies, returns `Result<T>` results
- **Thin Presentation Layer**: Parse commands → call services → render results
- **Strong Domain Model**: Value objects, domain errors, command objects
- **Configuration-Driven**: Externalize all constants and thresholds

## Refactor Phases

### Phase 1: Foundation & Modeling

#### 1.1 Add CSharpFunctionalExtensions Dependencies

Already injected to every project via "Directory.Packages.props".

#### 1.2 Create Error Model

#### 1.3 Extract Option Classes

Centralize all hardcoded constants into strongly-typed option classes.
Many already exists in `DataCollection.Infrastructure.Options`. Related loading logic is in `DataCollection.Presentation.Cli.Setup.BuilderHelpers`.

### Phase 2: Application Layer Purification

#### 2.1 Create Pure Service Interfaces

Define clean contracts that return `Result<T>` for all operations.

#### 2.2 Implement Service Layer Without UI Dependencies

Move all business logic from command classes into pure application services:

- Extract logic from `BugListDiscoveryCommands` into `BugListDiscoveryService`
- Extract logic from `ProcedureCommands` into `ProcedureService`  
- Remove all Spectre.Console and ConsoleAppFramework dependencies from Application layer

### Phase 3: Presentation Layer Simplification

#### 3.1 Extract Rendering Services

Create dedicated rendering classes for each result type.
One already exists in `DataCollection.Presentation.Cli.Rendering`. Enhance it for proper functional, clean code.

#### 3.2 Simplify Command Classes

Transform massive command classes into thin adapters that performs command validation and orchestration.

### Phase 4: Configuration & Dependency Injection

#### 4.1 Externalize All Configuration

Move hardcoded values to `appsettings.json`.

#### 4.2 Update Dependency Injection

Register new services and configuration:

```csharp
// Program.cs updates
services.AddOptionsFromOwnSectionAndValidateOnStart<DiscoveryConfiguration>(configuration);
services.AddScoped<IBugListDiscoveryService, BugListDiscoveryService>();
services.AddScoped<IResultRenderer<BugListDiscoveryAnalysis>, BugListDiscoveryRenderer>();
services.AddScoped<IErrorRenderer, ConsoleErrorRenderer>();
```

### Phase 5: Error Handling & Async Improvements

#### 5.1 Replace Exception Handling

Convert all `try/catch` blocks to use Result or Maybe.

#### 5.2 Improve Async Composition

Use CSharpFunctionalExtensions async utilities.

#### 5.3 Proper CancellationToken Propagation

Ensure all async operations respect cancellation.

### Phase 6: Simple Web API

A separate csproj using ASP.NET Core Minimal API, to provide another presentation layer.
This would also serve as an architecture test for the refactor outcome.

## Success Metrics

### Code Quality Improvements

- **File Size Reduction**: Target 70-80% reduction in command class sizes
- **Cyclomatic Complexity**: No methods over 10 complexity
- **Exception Handling**: Zero `catch (Exception ex)` blocks
- **Duplication**: Eliminate repeated rendering/validation logic

### Maintainability Improvements  

- **Testability**: 90%+ unit test coverage for application services
- **Configuration**: All magic numbers externalized
- **Separation of Concerns**: Clear boundaries between layers
- **Error Handling**: Explicit, composable error types

### Developer Experience

- **IDE Support**: Strong IntelliSense with `Fin<T>` return types
- **Debugging**: Clear error messages and stack traces
- **Documentation**: Self-documenting code through types
- **Extensibility**: Easy to add new discovery methods

## Risk Mitigation

### Technical Risks

- **Learning Curve**: Language-Ext functional concepts may be unfamiliar
  - *Mitigation*: Phased introduction, team training sessions
- **Performance Impact**: Functional patterns may introduce overhead
  - *Mitigation*: Benchmark before/after, optimize hot paths
- **Dependency Risk**: Taking dependency on Language-Ext
  - *Mitigation*: Well-maintained library, fallback plan available

### Business Risks  

- **Development Time**: Significant refactor may delay features
  - *Mitigation*: Incremental approach, maintain existing functionality
- **Regression Risk**: Major changes may introduce bugs
  - *Mitigation*: Comprehensive testing, gradual rollout

## Conclusion

This refactor will transform the DataCollection project from a procedural, tightly-coupled codebase into a functional, well-separated, maintainable system. The adoption of Language-Ext v5 provides powerful tools for error handling and async composition while improving code readability and testability.

The phased approach ensures minimal disruption to ongoing development while systematically addressing the identified code smells. The end result will be a more robust, testable, and maintainable codebase that serves as a foundation for future development.
