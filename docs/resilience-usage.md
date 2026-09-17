# File-Ferry - Resilience Usage

Status: Stub. Full content in Phase 7.0.

## Policies

- file-read    - FileResolver, ContentReader - existence, metadata, content reads
- path-search  - IPathSearchService - whole-PC filename search
- bundle-write - OutputSink - final output write

## Correlation

Every harvest job gets one correlation ID via CorrelationContext.NewId().
It flows through every log event, exception, and metric sample for the job.
