# Coding conventions

- Prefer file-scoped namespaces (`namespace Foo.Bar;`) over block-scoped namespaces (`namespace Foo.Bar { }`) in all C# files.
- All controller action methods (once an API is added) must include a `Name` property on their HTTP verb attribute (e.g. `[HttpGet("route", Name = nameof(MyAction))]`). NSwag uses the `Name` property to generate deterministic operation IDs in the SDK client.

# Structure

- `Wayroo.Payments.ConfigurationRecorder.Lambda` — the SQS-triggered worker lambda. `Function.cs` wires up
  Serilog JSON logging and a DI service provider, and validates required env vars (declared in
  `EnvironmentVariableKeys.cs`) on cold start.
- `Wayroo.Payments.Infrastructure` — the AWS CDK app that deploys the lambda (log group, SQS
  queue + DLQ, CloudWatch alarms). `Program.cs` -> `ResourceStack.cs` -> `Resources/*.cs`.
- `components.json` drives the CI pipeline's component discovery (lambda + infrastructure).
