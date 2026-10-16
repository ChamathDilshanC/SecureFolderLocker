using System.Text;
using Sfl.Cli.Hosting;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (IOException)
{
    // No console is attached (for example when launched without one); output still works.
}

CliContext context = CliContext.CreateDefault(args);
int exitCode = await CliHost.RunAsync(args, context);
context.Complete();
return exitCode;
