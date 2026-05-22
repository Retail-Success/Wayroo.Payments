# Wayroo.Payments

A Wayroo microservice for payments processing.

## Projects

| Project | Description |
| --- | --- |
| `Wayroo.Payments.ConfigurationRecorder.Lambda` | SQS-triggered .NET 10 lambda. For now it logs on startup and acknowledges every message; payment-handling logic will be added here. |
| `Wayroo.Payments.Infrastructure` | AWS CDK app that provisions the lambda and its supporting resources (log group, SQS queue + dead-letter queue, CloudWatch error alarms). |

## Building

```bash
dotnet build Wayroo.Payments.slnx
```

## Synthesizing the CloudFormation template

```bash
cdk synth --app "dotnet run --project Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure.csproj"
```

The generated template is written to `./cdk.out/PaymentsInfrastructureCDK.template.json`.

See `Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure/Readme.md` for more detail on the CDK project.

## Packaging the lambda

```bash
dotnet lambda package --project-location Wayroo.Payments.ConfigurationRecorder.Lambda/Wayroo.Payments.ConfigurationRecorder.Lambda
```

This produces `WayrooPayments.ConfigurationRecorderLambda.zip`, matching the `serviceName`/`componentName`
declared in `components.json`.
