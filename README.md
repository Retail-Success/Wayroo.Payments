# Wayroo.Payments

A Wayroo microservice for payments processing.

## Projects

| Project | Description |
| --- | --- |
| `Wayroo.Payments.ConfigurationRecorder.Lambda` | SQS-triggered .NET 10 lambda. For now it logs on startup and acknowledges every message; payment-handling logic will be added here. |
| `Wayroo.Payments.Eventing` | Publishes integration events to the `{env}-wayroo-events` intraprocess EventBridge bus (`IIntegrationEventPublisher` over `PutEvents`). See its [README](Wayroo.Payments.Eventing/Wayroo.Payments.Eventing/README.md) for the two-bus layout and the shape new consumers should take. |
| `Wayroo.Payments.Infrastructure` | AWS CDK app that provisions the lambda and its supporting resources (log group, SQS queue + dead-letter queue, CloudWatch error alarms). Both event buses are provisioned elsewhere and imported by ARN. |

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
