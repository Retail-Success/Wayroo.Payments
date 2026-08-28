# Wayroo.Payments.Infrastructure

Contains the code and classes that define and support the automated deployment of the
AWS resources needed for the Wayroo Payments service via AWS CloudFormation using
AWS Cloud Development Kit (aka CDK).

- **ResourceStack.cs** defines the infrastructure stack, incorporating all the necessary data
  and resources. This does all the real work in the project; **Program.cs** exists to call it,
  and all the other classes defined herein support it.
- **Resources** contains all the class files defining the individual components pulled into the stack.
  All these classes are internal to the project.

## Resources Excluded

The following resources were intentionally deemed outside of the scope of developer management
for the Wayroo Payments service resources:

- IAM Roles & Policies: As roles and policies have a large blast radius for unintended ramifications,
  they are intentionally not managed here, but rather managed by the Infrastructure team. The
  ConfigurationRecorder lambda assumes an existing `<environment>/WorkerRole`.

  Each construct documents the statements its role needs; the current asks are the permission lists
  in **ConfigurationRecorderLambda.cs** / **PaymentsAPI.cs**, plus publishing rights on the
  intraprocess event bus:

  ```json
  {
    "Effect": "Allow",
    "Action": "events:PutEvents",
    "Resource": "arn:aws:events:us-east-1:{account}:event-bus/{env}-wayroo-events"
  }
  ```

  Attach it to `<environment>/WorkerRole` (the recorder lambda publishes from D5 onward) and, once
  the API publishes, `<environment>-payments-service-role`. Scoping to that one bus ARN is the point
  of keeping the intraprocess bus separate from the webhook bus: `events:PutEvents` cannot be scoped
  by event `source`, so whatever can publish to a bus can forge any event on it.

## Event buses

Neither bus is created here. Both are provisioned by the Infrastructure team's common project and
imported by ARN through a stack parameter, so this stack attaches nothing to either one and creates
no EventBridge resources of its own.

| Bus | Parameter | Direction |
| --- | --- | --- |
| `{env}-webhook-bus` | `WebhookEventBusArn` | **inbound** — `WebhookEventBridgeRule` forwards events into the recorder lambda's source queue |
| `{env}-wayroo-events` | `WayrooEventsBusArn` | **outbound** — passed to the recorder lambda as an env var; its publisher `PutEvents` store-configuration changes here |

The intraprocess bus's archive, event-bus logging and the `events:PutEvents` grant above are the
Infrastructure team's to configure — `ByDesign.Infrastructure/modules/eventbus` is the reference
shape. If replay or bus logs are ever needed and missing, that is where to look, not here.

## Testing Locally

To generate the CloudFormation template locally:

1. Ensure you have the AWS CDK CLI installed https://docs.aws.amazon.com/cdk/v2/guide/getting-started.html#getting-started-install
2. From a command prompt at the root level of the solution, run

```bash
cdk synth --app "dotnet run --project Wayroo.Payments.Infrastructure\Wayroo.Payments.Infrastructure\Wayroo.Payments.Infrastructure.csproj"
```

mac

```bash
cdk synth --app "dotnet run --project Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure/Wayroo.Payments.Infrastructure.csproj"
```

3. If no errors occurred, the generated CloudFormation template can be found at `./cdk.out/PaymentsInfrastructureCDK.template.json`

## Deployments

When the pipeline runs, the AWS resources defined in this project will check if changes exist and if so,
the changes will attempt to be applied in each environment the pipeline runs against.

If issues are encountered, the pipeline may report a generic error about being unable to deploy the stack or
create a changeset. To troubleshoot, view the Events for the CloudFormation stack within the AWS Console
which will likely indicate the root cause of the issue. Some issues may result in the stack being in a `Rollback Failed`
state. The Infrastructure Team can manually continue the rollback if needed and should be engaged if this occurs.

## Additional References

- [AWS Guidance on CDK](https://docs.aws.amazon.com/cdk/v2/guide/home.html)
- [AWS Guidance on IAM Actions & Conditions](https://docs.aws.amazon.com/service-authorization/latest/reference/reference_policies_actions-resources-contextkeys.html)
  > Helpful when working with the Infrastructure team when defining new resources that will start to be deployed via CloudFormation
- [AWS Reference for CDK .Net](https://docs.aws.amazon.com/cdk/api/v2/dotnet/api/)
