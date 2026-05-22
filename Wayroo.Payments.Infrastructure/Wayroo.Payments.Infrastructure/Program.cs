using Amazon.CDK;

namespace Wayroo.Payments.Infrastructure;

sealed class Program
{
    public static void Main(string[] _)
    {
        var app = new App();

        Console.WriteLine("Creating stack...");

        new ResourceStack(
            app,
            "PaymentsInfrastructureCDK",
            new StackProps
            {
                Synthesizer = new DefaultStackSynthesizer(
                    new DefaultStackSynthesizerProps { GenerateBootstrapVersionRule = false }
                ),
            }
        );

        Console.WriteLine("Stack created");

        app.Synth();
    }
}
